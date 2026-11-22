using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Copixy
{
    /// <summary>
    /// Checks GitHub for a newer release. Only reads public release metadata
    /// (tag and page URL); nothing is sent except a standard HTTP request.
    /// </summary>
    static class Updater
    {
        public const string LatestApi = "https://api.github.com/repos/Deformarium/copixy/releases/latest";
        public const string ReleasesPage = "https://github.com/Deformarium/copixy/releases/latest";

        public class Result
        {
            public bool Ok;          // the check itself succeeded
            public bool Newer;       // a newer version is available
            public string Version;   // latest version, e.g. "1.1"
            public string Url;       // release page
            public string Error;
            public Problem Kind;     // why the check failed (for a clear message)
        }

        public enum Problem { None, Network, NoRelease, RateLimited, Other }

        /// <summary>Runs the check on a background thread; the callback is invoked on that thread.</summary>
        public static void CheckAsync(Action<Result> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Result r;
                try { r = Check(); }
                catch (WebException ex)
                {
                    var http = ex.Response as HttpWebResponse;
                    Problem k = Problem.Network;
                    if (http != null)
                    {
                        // 404: the repository is private or has no published release yet
                        if (http.StatusCode == HttpStatusCode.NotFound) k = Problem.NoRelease;
                        // 403/429: GitHub API limit for this IP (60 requests per hour)
                        else if ((int)http.StatusCode == 403 || (int)http.StatusCode == 429) k = Problem.RateLimited;
                        else k = Problem.Other;
                    }
                    r = new Result { Ok = false, Error = ex.Message, Kind = k };
                }
                catch (Exception ex) { r = new Result { Ok = false, Error = ex.Message, Kind = Problem.Other }; }
                done(r);
            });
        }

        static Result Check()
        {
            // the app targets .NET 4.x, whose defaults may not include TLS 1.2 (required by GitHub)
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

            var req = (HttpWebRequest)WebRequest.Create(LatestApi);
            req.UserAgent = "Copixy/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;

            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = rd.ReadToEnd();

            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            if (!tag.Success) return new Result { Ok = false, Error = "No release information", Kind = Problem.NoRelease };
            var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+/releases/tag/[^\"]+)\"");

            string latest = tag.Groups[1].Value.Trim();
            Version lv, cv;
            if (!TryParse(latest, out lv) || !TryParse(AppInfo.Version, out cv))
                return new Result { Ok = false, Error = "Unrecognized version: " + latest, Kind = Problem.Other };

            return new Result
            {
                Ok = true,
                Newer = lv > cv,
                Version = Short(lv),
                Url = url.Success ? url.Groups[1].Value : ReleasesPage
            };
        }

        /// <summary>Parses "v1.2", "1.2.3", "Copixy 1.2" into a comparable Version (missing parts = 0).</summary>
        static bool TryParse(string s, out Version v)
        {
            v = null;
            var m = Regex.Match(s ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return false;
            int a = int.Parse(m.Groups[1].Value);
            int b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            int c = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            v = new Version(a, b, c);
            return true;
        }

        static string Short(Version v)
        {
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }
    }
}
