﻿using Jackett.Models;
using NLog;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jackett.Services
{
    public interface ILogCacheService
    {
       // void AddLog(LogEventInfo l);
        List<CachedLog> Logs { get; }
    }

    [Target("LogService")]
    class LogCacheService: TargetWithLayout, ILogCacheService
    {
        private static List<CachedLog> logs = new List<CachedLog>();

        public void AddLog(LogEventInfo l)
        {
            lock (logs)
            {
                logs.Insert(0, new CachedLog()
                {
                    Level = l.Level.Name,
                    Message = l.Message,
                    When = l.TimeStamp 
                });
                logs = logs.Take(50).ToList();
            }

        }

        public List<CachedLog> Logs
        {
            get
            {
                lock (logs)
                {
                    return logs.ToList();
                }
            }
        }

        protected override void Write(LogEventInfo logEvent)
        {
            AddLog(logEvent);
        }
    }
}
