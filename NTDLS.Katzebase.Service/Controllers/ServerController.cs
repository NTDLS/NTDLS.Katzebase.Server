﻿using Microsoft.AspNetCore.Mvc;
using NTDLS.Katzebase.Client.Payloads;

namespace NTDLS.Katzebase.Client.Service.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServerController
    {
        /// <summary>
        /// Tests the connection to the server.
        /// </summary>
        /// <param name="schema"></param>
        [HttpGet]
        [Route("{sessionId}/Ping")]
        public KbActionResponsePing Ping(Guid sessionId)
        {
            try
            {
                var processId = Program.Core.Sessions.UpsertSessionId(sessionId);
                Thread.CurrentThread.Name = Thread.CurrentThread.Name = $"KbAPI:{processId}:{KbUtility.GetCurrentMethod()}";
                Program.Core.Log.Trace(Thread.CurrentThread.Name);

                var result = new KbActionResponsePing
                {
                    ProcessId = processId,
                    SessionId = sessionId,
                    ServerTimeUTC = DateTime.UtcNow,
                    Success = true
                };

                return result;
            }
            catch (Exception ex)
            {
                return new KbActionResponsePing
                {
                    ExceptionText = ex.Message,
                    Success = false
                };
            }
        }

        /// <summary>
        /// Tests the connection to the server.
        /// </summary>
        /// <param name="schema"></param>
        [HttpGet]
        [Route("{sessionId}/Ping/{clientName}")]
        public KbActionResponsePing Ping(Guid sessionId, string clientName)
        {
            try
            {
                var processId = Program.Core.Sessions.UpsertSessionId(sessionId, clientName);
                Thread.CurrentThread.Name = Thread.CurrentThread.Name = $"KbAPI:{processId}:{KbUtility.GetCurrentMethod()}";
                Program.Core.Log.Trace(Thread.CurrentThread.Name);

                var result = new KbActionResponsePing
                {
                    ProcessId = processId,
                    SessionId = sessionId,
                    ServerTimeUTC = DateTime.UtcNow,
                    Success = true
                };

                return result;
            }
            catch (Exception ex)
            {
                return new KbActionResponsePing
                {
                    ExceptionText = ex.Message,
                    Success = false
                };
            }
        }

        /// <summary>
        /// Tests the connection to the server.
        /// </summary>
        /// <param name="schema"></param>
        [HttpGet]
        [Route("{sessionId}/CloseSession")]
        public KbActionResponsePing CloseSession(Guid sessionId)
        {
            try
            {
                var processId = Program.Core.Sessions.UpsertSessionId(sessionId);
                Thread.CurrentThread.Name = Thread.CurrentThread.Name = $"KbAPI:{processId}:{KbUtility.GetCurrentMethod()}";
                Program.Core.Log.Trace(Thread.CurrentThread.Name);

                Program.Core.Sessions.CloseByProcessId(processId);

                var result = new KbActionResponsePing
                {
                    ProcessId = processId,
                    SessionId = sessionId,
                    ServerTimeUTC = DateTime.UtcNow,
                    Success = true
                };

                return result;
            }
            catch (Exception ex)
            {
                return new KbActionResponsePing
                {
                    ExceptionText = ex.Message,
                    Success = false
                };
            }
        }

        /// <summary>
        /// Tests the connection to the server.
        /// </summary>
        /// <param name="schema"></param>
        [HttpGet]
        [Route("{sessionId}/TerminateProcess/{referencedProcessId}")]
        public KbActionResponsePing KillProcess(Guid sessionId, ulong referencedProcessId)
        {
            try
            {
                var processId = Program.Core.Sessions.UpsertSessionId(sessionId);
                Thread.CurrentThread.Name = Thread.CurrentThread.Name = $"KbAPI:{processId}:{KbUtility.GetCurrentMethod()}";
                Program.Core.Log.Trace(Thread.CurrentThread.Name);

                Program.Core.Sessions.CloseByProcessId(referencedProcessId);

                var result = new KbActionResponsePing
                {
                    ProcessId = processId,
                    SessionId = sessionId,
                    ServerTimeUTC = DateTime.UtcNow,
                    Success = true
                };

                return result;
            }
            catch (Exception ex)
            {
                return new KbActionResponsePing
                {
                    ExceptionText = ex.Message,
                    Success = false
                };
            }
        }
    }
}
