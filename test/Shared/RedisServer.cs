//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.Web.Redis.FunctionalTests
{
    internal class RedisServer : IDisposable
    {
        private static Process _server;
        private static int _refCount = 0;
        private static readonly object _lockObj = new object();
        private static bool _isServerRunning = false;
        private bool _hasSubscribed = false;

        private static void WaitForRedisToStart()
        {
            // if redis is not up in 2 seconds time than return failure
            for (int i = 0; i < 200; i++)
            {
                Thread.Sleep(10);
                try
                {
                    Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Connect("localhost", 6379);
                    socket.Close();
                    LogUtility.LogInfo("Successfully connected to Redis server after Time: {0} ms", (i+1) * 10);
                    return;
                }
                catch
                {}
            }
            LogUtility.LogInfo("Failed to connect to Redis server after 2 seconds");
        }

        public static bool IsRedisRunning()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.Connect("localhost", 6379);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public RedisServer()
        {
            lock (_lockObj)
            {
                if (!_isServerRunning)
                {
                    StartRedisServer();
                }
                
                // Increment reference count
                _refCount++;
                _hasSubscribed = true;
                LogUtility.LogInfo($"Redis server subscription added. Current subscribers: {_refCount}");
            }
        }

        private static void StartRedisServer()
        {
            if (_isServerRunning)
                return;

            // First check if Redis is already running
            if (IsRedisRunning())
            {
                LogUtility.LogInfo("Redis server already running - using existing instance");
                _isServerRunning = true;
                return;
            }

            try
            {
                KillRedisServers(); // Make sure there are no rogue servers
                
                _server = new Process();
                string executable_path = $"{Environment.CurrentDirectory}\\..\\..\\..\\..\\..\\redis-server.exe";
                LogUtility.LogInfo($"Starting Redis server from: {executable_path}");
                
                _server.StartInfo.FileName = executable_path;
                _server.StartInfo.Arguments = "--maxmemory 20000000";
                _server.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                
                // Set up redirects for log capture
                _server.StartInfo.UseShellExecute = false;
                _server.StartInfo.RedirectStandardOutput = true;
                _server.StartInfo.RedirectStandardError = true;
                
                _server.Start();
                WaitForRedisToStart();
                
                _isServerRunning = true;
                LogUtility.LogInfo("Redis server started successfully");
            }
            catch (Exception ex)
            {
                LogUtility.LogInfo($"Failed to start Redis server: {ex.Message}");
                _isServerRunning = false;
                throw;
            }
        }

        // Make sure that no redis-server instance is running
        public static void KillRedisServers()
        {
            foreach (var proc in Process.GetProcessesByName("redis-server"))
            {
                try
                {
                    proc.Kill();
                    LogUtility.LogInfo($"Killed existing Redis server process: {proc.Id}");
                }
                catch (Exception ex)
                {
                    LogUtility.LogInfo($"Failed to kill Redis server process: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (!_hasSubscribed)
                return;
            
            lock (_lockObj)
            {
                if (_refCount > 0)
                {
                    _refCount--;
                    LogUtility.LogInfo($"Redis server subscription removed. Remaining subscribers: {_refCount}");
                }

                _hasSubscribed = false;

                // Only shut down the server when no more subscribers
                if (_refCount == 0 && _isServerRunning)
                {
                    try
                    {
                        if (_server != null && !_server.HasExited)
                        {
                            _server.Kill();
                            _server.Dispose();
                            _server = null;
                        }
                        LogUtility.LogInfo("Redis server stopped");
                    }
                    catch (Exception ex)
                    {
                        LogUtility.LogInfo($"Error stopping Redis server: {ex.Message}");
                    }
                    finally
                    {
                        _isServerRunning = false;
                    }
                }
            }
        }
    }
}
