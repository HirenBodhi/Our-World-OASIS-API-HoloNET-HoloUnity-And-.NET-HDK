﻿using System;
using System.Collections.Generic;
using System.Text;

namespace NextGenSoftware.Holochain.HoloNET.Client
{
    public class NLogger : ILogger
    {
        public void Log(string message, LogType type)
        {
            var logger = NLog.LogManager.GetCurrentClassLogger();
            //NLog.LogManager.Shutdown();
            //logger.ConditionalDebug()
            //logger.Log()

            switch (type)
            {
                case LogType.Debug:
                    logger.Debug(message);
                    break;

                case LogType.Error:
                    logger.Error(message);
                    break;

                case LogType.Info:
                    logger.Info(message);
                    break;

                case LogType.Warn:
                    logger.Warn(message);
                    break;
            }  
        }
    }
}
