﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using QuartzNetWebConsole.Utils;

namespace QuartzNetWebConsole {
    public static class Setup {
        /// <summary>
        /// What Quartz.NET scheduler should the web console use.
        /// </summary>
        public static Func<IScheduler> Scheduler { get; set; }

        private static ILogger logger;

        /// <summary>
        /// Optional logger to attach to the web console
        /// </summary>
        public static ILogger Logger {
            get { return logger; }
            set {
                var scheduler = Scheduler();
                if (logger != null) {
                    IJobListener jobListener = logger;
                    ITriggerListener triggerListener = logger;
                    scheduler.ListenerManager.RemoveJobListener(jobListener.Name);
                    scheduler.ListenerManager.RemoveTriggerListener(triggerListener.Name);
                    scheduler.ListenerManager.RemoveSchedulerListener(logger);
                }
                if (value != null) {
                    scheduler.ListenerManager.AddJobListener(value);
                    //scheduler.ListenerManager.AddJobListenerMatcher()
                    scheduler.ListenerManager.AddTriggerListener(value);
                    scheduler.ListenerManager.AddSchedulerListener(value);
                }
                logger = value;
            }
        }

        static Setup() {
            Scheduler = () => { throw new Exception("Define QuartzNetWebConsole.Setup.Scheduler"); };
        }

        private static Uri GetOwinUri(this IDictionary<string, object> env) {
            var headers = (IDictionary<string, string[]>)env["owin.RequestHeaders"];
            var scheme = (string)env["owin.RequestScheme"];
            var hostAndPort = headers["Host"].First().Split(':');
            var host = hostAndPort[0];
            var port = hostAndPort.Length > 1 ? int.Parse(hostAndPort[1]) : (scheme == Uri.UriSchemeHttp ? 80 : 443);
            var path = (string)env["owin.RequestPathBase"] + (string)env["owin.RequestPath"];
            var query = (string)env["owin.RequestQueryString"];

            var uriBuilder = new UriBuilder(scheme: scheme, host: host, portNumber: port) {
                Path = path,
                Query = query,
            };

            return uriBuilder.Uri;
        }

        private static Stream GetOwinResponseBody(this IDictionary<string, object> env) {
            return (Stream) env["owin.ResponseBody"];
        }

        private static IDictionary<string, string[]> GetOwinResponseHeaders(this IDictionary<string, object> env) {
            return (IDictionary<string, string[]>) env["owin.ResponseHeaders"];
        }

        private static void SetOwinContentType(this IDictionary<string, object> env, string contentType) {
            if (string.IsNullOrEmpty(contentType))
                return;
            env.GetOwinResponseHeaders()["Content-Type"] = new [] {contentType};
        }

        private static void SetOwinContentLength(this IDictionary<string, object> env, long length) {
            env.GetOwinResponseHeaders()["Content-Length"] = new[] { length.ToString() };
        }

        private static void SetOwinStatusCode(this IDictionary<string, object> env, int statusCode) {
            env["owin.ResponseStatusCode"] = statusCode;
        }

        public delegate Task AppFunc(IDictionary<string, object> env);

        private static AppFunc EvaluateResponse(Response response) {
            return env => response.Match(
                content: async x => {
                    env.SetOwinContentType(x.ContentType);
                    env.SetOwinContentLength(x.Content.Length);
                    var sw = new StreamWriter(env.GetOwinResponseBody());
                    await sw.WriteAsync(x.Content);
                    await sw.FlushAsync();
                },
                xdoc: async x => {
                    env.SetOwinContentType(x.ContentType);
                    var content = x.Content.ToString();
                    env.SetOwinContentLength(content.Length);
                    var sw = new StreamWriter(env.GetOwinResponseBody());
                    await sw.WriteAsync(content);
                    await sw.FlushAsync();
                },
                redirect: async x => {
                    env.SetOwinStatusCode(302);
                    env.GetOwinResponseHeaders()["Location"] = new [] {x.Location};
                    await Task.Yield();
                });
        }

        public static Func<AppFunc, AppFunc> Owin(Func<IScheduler> scheduler) {
            Setup.Scheduler = scheduler;
            return app => env => {
                var uri = env.GetOwinUri();
                var response =
                    Routing.Routes
                        .Where(x => uri.AbsolutePath.Split('.')[0].EndsWith(x.Key, StringComparison.InvariantCultureIgnoreCase))
                        .Select(r => r.Value(uri))
                        .Select(EvaluateResponse)
                        .FirstOrDefault();
                return response == null ? app(env) : response(env);
            };
        }
    }
}