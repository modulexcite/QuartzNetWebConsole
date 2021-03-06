﻿using System;
using System.Collections.Specialized;
using System.Linq;
using System.Xml.Linq;
using Quartz;
using Quartz.Impl.Matchers;
using QuartzNetWebConsole.Utils;
using QuartzNetWebConsole.Views;

namespace QuartzNetWebConsole.Controllers {
    public class JobGroupController {
        public static Response Execute(Uri url, Func<ISchedulerWrapper> getScheduler) {
            var scheduler = getScheduler();
            var querystring = url.ParseQueryString();
            var group = querystring["group"];
            var jobNames = scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
            var runningJobs = scheduler.GetCurrentlyExecutingJobs();
            var jobs = jobNames.Select(j => {
                var job = scheduler.GetJobDetail(j);
                var interruptible = typeof (IInterruptableJob).IsAssignableFrom(job.JobType);
                var jobContext = runningJobs.FirstOrDefault(r => r.JobDetail.Key.ToString() == job.Key.ToString());
                return new JobWithContext(job, jobContext, interruptible);
            });
            var paused = scheduler.IsJobGroupPaused(group);
            var highlight = querystring["highlight"];
            var view = Views.Views.JobGroup(group, paused, highlight, url.PathAndQuery, jobs);
            return new Response.XDocumentResponse(Helpers.XHTML(view));
        }
    }
}