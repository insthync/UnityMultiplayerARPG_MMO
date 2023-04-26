﻿using System.Collections.Generic;
using UnityEngine;
using LiteNetLibManager;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Concurrent;
using ConcurrentCollections;

namespace MultiplayerARPG.MMO
{
#if (UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE
    [DefaultExecutionOrder(-895)]
#endif
    public partial class MapSpawnNetworkManager : LiteNetLibManager.LiteNetLibManager, IAppServer
    {
#if (UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE
        [Header("Central Network Connection")]
#endif
        public string clusterServerAddress = "127.0.0.1";
        public int clusterServerPort = 6010;
        public string machineAddress = "127.0.0.1";

#if (UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE
        [Header("Map Spawn Settings")]
#endif
        public string exePath = "./Build.exe";
        public bool notSpawnInBatchMode = false;
        public int startPort = 8000;
        public List<BaseMapInfo> spawningMaps;

#if (UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE
        [Header("Running In Editor")]
#endif
        public bool isOverrideExePath;
        public string overrideExePath = "./Build.exe";
        public bool editorNotSpawnInBatchMode;

        private int _spawningPort = -1;
        private int _portCounter = -1;
        /// <summary>
        /// Free ports which can use for start map server
        /// </summary>
        private readonly ConcurrentQueue<int> _freePorts = new ConcurrentQueue<int>();
        /// <summary>
        /// Actions which will invokes in main thread
        /// </summary>
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        /// <summary>
        /// Map servers processes id
        /// </summary>
        private readonly ConcurrentHashSet<int> _processes = new ConcurrentHashSet<int>();
        /// <summary>
        /// List of Map servers that restarting in update loop
        /// </summary>
        private readonly ConcurrentQueue<string> _restartingScenes = new ConcurrentQueue<string>();

        public string ExePath
        {
            get
            {
                if (Application.isEditor && isOverrideExePath)
                    return overrideExePath;
                else
                    return exePath;
            }
        }

        public bool NotSpawnInBatchMode
        {
            get
            {
                if (Application.isEditor)
                    return editorNotSpawnInBatchMode;
                else
                    return notSpawnInBatchMode;
            }
        }

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        public ClusterClient ClusterClient { get; private set; }
#endif
        public string ClusterServerAddress { get { return clusterServerAddress; } }
        public int ClusterServerPort { get { return clusterServerPort; } }
        public string AppAddress { get { return machineAddress; } }
        public int AppPort { get { return networkPort; } }
        public string AppExtra { get { return string.Empty; } }
        public CentralServerPeerType PeerType { get { return CentralServerPeerType.MapSpawnServer; } }

#if NET || NETCOREAPP
        public MapSpawnNetworkManager() : base()
        {
            Initialize();
        }
#else
        protected override void Start()
        {
            Initialize();
            base.Start();
        }
#endif

        protected virtual void Initialize()
        {
            useWebSocket = false;
            maxConnections = int.MaxValue;
#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
            ClusterClient = new ClusterClient(this);
            ClusterClient.onResponseAppServerRegister = OnResponseAppServerRegister;
            ClusterClient.RegisterRequestHandler<RequestSpawnMapMessage, ResponseSpawnMapMessage>(MMORequestTypes.RequestSpawnMap, HandleRequestSpawnMap);
#endif
        }

        protected virtual void Clean()
        {
            this.InvokeInstanceDevExtMethods("Clean");
            _spawningPort = -1;
            _portCounter = -1;
            // Clear free ports
            while (_freePorts.TryDequeue(out _))
            {
                // Do nothing
            }
            // Clear main thread actions
            while (_mainThreadActions.TryDequeue(out _))
            {
                // Do nothing
            }
            // Clear processes
            List<int> processIds = new List<int>(_processes);
            foreach (int processId in processIds)
            {
                Process.GetProcessById(processId).Kill();
                _processes.TryRemove(processId);
            }
            // Clear restarting scenes
            while (_restartingScenes.TryDequeue(out _))
            {
                // Do nothing
            }
        }

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        public override void OnStartServer()
        {
            this.InvokeInstanceDevExtMethods("OnStartServer");
            ClusterClient.OnAppStart();
            _spawningPort = startPort;
            _portCounter = startPort;
            base.OnStartServer();
        }
#endif

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        public override void OnStopServer()
        {
            ClusterClient.OnAppStop();
            Clean();
            base.OnStopServer();
        }
#endif

        public override void OnStartClient(LiteNetLibClient client)
        {
            this.InvokeInstanceDevExtMethods("OnStartClient", client);
            base.OnStartClient(client);
        }

        public override void OnStopClient()
        {
            if (!IsServer)
                Clean();
            base.OnStopClient();
        }

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (IsServer)
            {
                ClusterClient.Update();
                if (ClusterClient.IsAppRegistered)
                {
                    if (_restartingScenes.Count > 0)
                    {
                        string tempRestartingScenes;
                        while (_restartingScenes.TryDequeue(out tempRestartingScenes))
                        {
                            SpawnMap(tempRestartingScenes, true);
                        }
                    }
                }
                if (_mainThreadActions.Count > 0)
                {
                    Action tempMainThreadAction;
                    while (_mainThreadActions.TryDequeue(out tempMainThreadAction))
                    {
                        if (tempMainThreadAction != null)
                            tempMainThreadAction.Invoke();
                    }
                }
            }
        }
#endif

        protected override void OnDestroy()
        {
            Clean();
            base.OnDestroy();
        }

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        internal async UniTaskVoid HandleRequestSpawnMap(
            RequestHandlerData requestHandler,
            RequestSpawnMapMessage request,
            RequestProceedResultDelegate<ResponseSpawnMapMessage> result)
        {
            await UniTask.Yield();
            UITextKeys message = UITextKeys.NONE;
            if (!ClusterClient.IsAppRegistered)
                message = UITextKeys.UI_ERROR_APP_NOT_READY;
            else if (string.IsNullOrEmpty(request.mapId))
                message = UITextKeys.UI_ERROR_EMPTY_SCENE_NAME;

            if (message != UITextKeys.NONE)
            {
                result.InvokeError(new ResponseSpawnMapMessage()
                {
                    message = message
                });
            }
            else
            {
                SpawnMap(request, result, false);
            }
        }
#endif

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        private void OnResponseAppServerRegister(AckResponseCode responseCode)
        {
            if (responseCode != AckResponseCode.Success)
                return;
            if (spawningMaps == null || spawningMaps.Count == 0)
            {
                spawningMaps = new List<BaseMapInfo>();
                spawningMaps.AddRange(GameInstance.MapInfos.Values);
            }
            SpawnMaps(spawningMaps).Forget();
        }
#endif

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        private async UniTaskVoid SpawnMaps(List<BaseMapInfo> spawningMaps)
        {
            foreach (BaseMapInfo map in spawningMaps)
            {
                SpawnMap(map.Id, true);
                // Add some delay before spawn next map
                await UniTask.Delay(100, true);
            }
        }
#endif

        private void FreePort(int port)
        {
            _freePorts.Enqueue(port);
        }

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        private void SpawnMap(
            RequestSpawnMapMessage message,
            RequestProceedResultDelegate<ResponseSpawnMapMessage> result,
            bool autoRestart)
        {
            SpawnMap(message.mapId, autoRestart, message, result);
        }
#endif

#if NET || NETCOREAPP || ((UNITY_EDITOR || UNITY_SERVER) && UNITY_STANDALONE)
        private void SpawnMap(
            string mapId, bool autoRestart,
            RequestSpawnMapMessage? request = null,
            RequestProceedResultDelegate<ResponseSpawnMapMessage> result = null)
        {
            // Port to run map server
            if (_freePorts.Count > 0)
            {
                _freePorts.TryDequeue(out _spawningPort);
            }
            else
            {
                _spawningPort = _portCounter++;
            }
            int port = _spawningPort;

            // Path to executable
            string path = ExePath;
            if (string.IsNullOrEmpty(path))
            {
                path = File.Exists(Environment.GetCommandLineArgs()[0])
                    ? Environment.GetCommandLineArgs()[0]
                    : Process.GetCurrentProcess().MainModule.FileName;
            }

            if (LogInfo)
                Logging.Log(LogTag, "Starting process from: " + path);

            // Spawning Process Info
            ProcessStartInfo startProcessInfo = new ProcessStartInfo(path)
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                Arguments = (!NotSpawnInBatchMode ? "-batchmode -nographics " : string.Empty) +
                    $"{MMOServerInstance.ARG_MAP_ID} {mapId} " +
                    (request.HasValue ?
                        $"{MMOServerInstance.ARG_INSTANCE_ID} {request.Value.instanceId} " +
                        $"{MMOServerInstance.ARG_INSTANCE_POSITION_X} {request.Value.instanceWarpPosition.x} " +
                        $"{MMOServerInstance.ARG_INSTANCE_POSITION_Y} {request.Value.instanceWarpPosition.y} " +
                        $"{MMOServerInstance.ARG_INSTANCE_POSITION_Z} {request.Value.instanceWarpPosition.z} " +
                        $"{(request.Value.instanceWarpOverrideRotation ? MMOServerInstance.ARG_INSTANCE_OVERRIDE_ROTATION : string.Empty)} " +
                        $"{MMOServerInstance.ARG_INSTANCE_ROTATION_X} {request.Value.instanceWarpRotation.x} " +
                        $"{MMOServerInstance.ARG_INSTANCE_ROTATION_Y} {request.Value.instanceWarpRotation.y} " +
                        $"{MMOServerInstance.ARG_INSTANCE_ROTATION_Z} {request.Value.instanceWarpRotation.z} "
                        : string.Empty) +
                    $"{MMOServerInstance.ARG_CENTRAL_ADDRESS} {clusterServerAddress} " +
                    $"{MMOServerInstance.ARG_CENTRAL_PORT} {clusterServerPort} " +
                    $"{MMOServerInstance.ARG_MACHINE_ADDRESS} {machineAddress} " +
                    $"{MMOServerInstance.ARG_MAP_PORT} {port} " +
                    $"{MMOServerInstance.ARG_START_MAP_SERVER} ",
            };

            if (LogInfo)
                Logging.Log(LogTag, "Starting process with args: " + startProcessInfo.Arguments);

            int processId = 0;
            bool processStarted = false;
            try
            {
                new Thread(() =>
                {
                    try
                    {
                        using (Process process = Process.Start(startProcessInfo))
                        {
                            processId = process.Id;
                            _processes.Add(processId);

                            processStarted = true;

                            _mainThreadActions.Enqueue(() =>
                            {
                                if (LogInfo)
                                    Logging.Log(LogTag, "Process started. Id: " + processId);
                                // Notify server that it's successfully handled the request
                                if (request.HasValue && result != null)
                                {
                                    result.InvokeSuccess(new ResponseSpawnMapMessage()
                                    {
                                        message = UITextKeys.NONE,
                                        instanceId = request.Value.instanceId,
                                        requestId = request.Value.requestId,
                                    });
                                }
                            });
                            process.WaitForExit();
                        }
                    }
                    catch (Exception e)
                    {
                        if (!processStarted)
                        {
                            _mainThreadActions.Enqueue(() =>
                            {
                                if (LogFatal)
                                {
                                    Logging.LogError(LogTag, "Tried to start a process at: '" + path + "' but it failed. Make sure that you have set correct the 'exePath' in 'MapSpawnNetworkManager' component");
                                    Logging.LogException(LogTag, e);
                                }

                                // Notify server that it failed to spawn map scene handled the request
                                if (request.HasValue && result != null)
                                {
                                    result.InvokeError(new ResponseSpawnMapMessage()
                                    {
                                        message = UITextKeys.UI_ERROR_CANNOT_EXCUTE_MAP_SERVER,
                                        instanceId = request.Value.instanceId,
                                        requestId = request.Value.requestId,
                                    });
                                }
                            });
                        }
                    }
                    finally
                    {
                        // Remove the process
                        _processes.TryRemove(processId);

                        // Restarting scene
                        if (autoRestart)
                            _restartingScenes.Enqueue(mapId);

                        _mainThreadActions.Enqueue(() =>
                        {
                            // Release the port number
                            FreePort(port);

                            if (LogInfo)
                                Logging.Log(LogTag, "Process spawn id: " + processId + " killed.");
                        });
                    }

                }).Start();
            }
            catch (Exception e)
            {
                if (request.HasValue && result != null)
                {
                    result.InvokeError(new ResponseSpawnMapMessage()
                    {
                        message = UITextKeys.UI_ERROR_UNKNOW,
                        instanceId = request.Value.instanceId,
                        requestId = request.Value.requestId,
                    });
                }

                // Restarting scene
                if (autoRestart)
                    _restartingScenes.Enqueue(mapId);

                if (LogFatal)
                    Logging.LogException(LogTag, e);
            }
        }
#endif
    }
}
