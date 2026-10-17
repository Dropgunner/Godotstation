using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Movement;
using Godotstation.Engine.Rounds;

namespace Godotstation.Game.Playtest;

/// <summary>LAN playtest adapter: gathers input, asks the server movement engine, and draws the station.</summary>
public partial class StationPlaytestController : Node2D
{
    private const int MaximumPlayers = 4;
    private const float SnapshotInterval = 0.05f;

    private readonly Dictionary<int, PlayerRuntime> players = new();
    private readonly CrewRoster crewRoster = StationCrewJobCatalog.CreateRoster();
    private readonly SharedRoundLifecycle roundLifecycle = new();
    private CollisionMap collisionMap = null!;
    private MovementProcessor movementProcessor = null!;
    private SharedStationInventoryWorld sharedInteractionWorld = null!;
    private StationMapView stationMapView = null!;
    private DynamicObject fixtureObstacle = null!;
    private DynamicObject girderObstacle = null!;
    private int? wrenchOwner;
    private Vector2 wrenchWorldPosition = new(-1, 2);
    private Vector2 fixtureWorldPosition = new(0, 2);
    private bool fixtureAnchored = true;
    private bool engineeringAirlockOpen;
    private Label statusLabel = null!;
    private Label activeHandLabel = null!;
    private Label loadoutLabel = null!;
    private Button startShiftButton = null!;
    private RichTextLabel chatLog = null!;
    private LineEdit chatInput = null!;
    private int localPeerId;
    private float snapshotTime;
    private float constructionSnapshotTime;
    private ulong smokeMoveUntilMsec;
    private bool smokeMoveStarted;
    private bool smokeChatStarted;
    private bool smokeInteractionPickupRequested;
    private bool smokeInteractionUseRequested;
    private bool smokeInteractionDropRequested;
    private bool smokeInteractionsCompleted;
    private bool smokeConstructionPickupRequested;
    private bool smokeConstructionStarted;
    private bool smokeConstructionCompleted;
    private bool smokeEngineeringAccessRequested;
    private bool smokeEngineeringAccessDenied;
    private bool smokeEngineerDoorRequestSent;
    private bool smokeEngineeringAccessVerified;
    private int smokeMetalOwner;
    private int smokeSheetCount = 2;
    private bool smokeGirderPlated;
    private bool localRoundStarted;
    private string connectionStatus = "Connecting…";
    private string hostAddress = "LAN IP unavailable";

    public override void _Ready()
    {
        SetMultiplayerAuthority(1);
        collisionMap = StationPlaytestLayout.CreateCollisionMap();
        movementProcessor = new MovementProcessor(collisionMap);
        sharedInteractionWorld = new SharedStationInventoryWorld(new StationReachabilityMap(collisionMap));
        sharedInteractionWorld.StateChanged += OnSharedStateChanged;
        sharedInteractionWorld.ConstructionStateChanged += OnConstructionStateChanged;
        sharedInteractionWorld.EngineeringAirlockStateChanged += OnEngineeringAirlockStateChanged;
        collisionMap.SetTile(8, -4, sharedInteractionWorld.EngineeringAirlockPassability);
        fixtureObstacle = new DynamicObject(0, 2, isPushable: false, tag: "service-cart");
        collisionMap.AddObject(fixtureObstacle);
        girderObstacle = new DynamicObject(2, 2, isPushable: false, tag: "girder");
        collisionMap.AddObject(girderObstacle);
        BuildStationView();
        BuildHud();

        var multiplayer = GetMultiplayer();
        multiplayer.PeerConnected += peerId => OnPeerConnected((int)peerId);
        multiplayer.PeerDisconnected += peerId => OnPeerDisconnected((int)peerId);
        multiplayer.ConnectedToServer += OnConnectedToServer;
        multiplayer.ConnectionFailed += OnConnectionFailed;
        multiplayer.ServerDisconnected += OnServerDisconnected;

        if (PlaytestLaunchOptions.IsHost)
            StartHost();
        else
            JoinHost(PlaytestLaunchOptions.Address);
    }

    public override void _PhysicsProcess(double delta)
    {
        var multiplayer = GetMultiplayer();
        if (multiplayer.MultiplayerPeer == null || multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
            return;

        localPeerId = multiplayer.GetUniqueId();
        if (PlaytestLaunchOptions.SmokeChat && !smokeChatStarted && players.Count >= 2 && localRoundStarted)
        {
            smokeChatStarted = true;
            SubmitSpeech($"SMOKE_CHAT_PEER_{localPeerId}");
        }
        DriveSmokeInteractions();
        DriveSmokeConstruction();
        DriveSmokeEngineeringAccess();
        var direction = ReadLocalInput();

        if (multiplayer.IsServer())
        {
            if (players.ContainsKey(localPeerId))
                SetMovementInput(localPeerId, localPeerId, direction.X, direction.Y);

            float step = (float)Math.Min(delta, 0.1);
            constructionSnapshotTime += step;
            bool publishConstructionProgress = constructionSnapshotTime >= SnapshotInterval;
            if (publishConstructionProgress)
                constructionSnapshotTime %= SnapshotInterval;
            foreach (PlayerRuntime player in players.Values)
            {
                float x = player.Position.X;
                float y = player.Position.Y;
                var intent = new MoveIntent
                {
                    DeltaX = player.DeltaX,
                    DeltaY = player.DeltaY,
                    Timestamp = Time.GetTicksMsec() / 1000.0
                };

                if (intent.DeltaX != 0 || intent.DeltaY != 0)
                    movementProcessor.TryMove(player.Movement, ref x, ref y, intent, step);
                else
                    movementProcessor.ApplyMomentum(player.Movement, ref x, ref y, step);

                if (Mathf.RoundToInt(x) != Mathf.RoundToInt(player.Position.X) ||
                    Mathf.RoundToInt(y) != Mathf.RoundToInt(player.Position.Y))
                    collisionMap.MoveObject(player.Obstacle, Mathf.RoundToInt(x), Mathf.RoundToInt(y));

                player.Position = new Vector2(x, y);
                sharedInteractionWorld.SetActorPosition(player.PeerId, new System.Numerics.Vector2(x, y));
                sharedInteractionWorld.AdvanceConstruction(player.PeerId, step, publishConstructionProgress);
                if (fixtureObstacle.X != Mathf.RoundToInt(fixtureWorldPosition.X) ||
                    fixtureObstacle.Y != Mathf.RoundToInt(fixtureWorldPosition.Y))
                {
                    fixtureWorldPosition = new Vector2(fixtureObstacle.X, fixtureObstacle.Y);
                    sharedInteractionWorld.SetFixturePosition(new System.Numerics.Vector2(fixtureObstacle.X, fixtureObstacle.Y));
                }
                player.Avatar.Position = player.Position;
                if (!player.HasLoggedMovement && player.Position.DistanceSquaredTo(player.SpawnPosition) > 0.08f)
                {
                    player.HasLoggedMovement = true;
                    GD.Print($"PLAYTEST_POSITION peer={player.PeerId} x={x:0.00} y={y:0.00}");
                    if (player.PeerId != 1)
                        GD.Print($"PLAYTEST_CLIENT_POSITION peer={player.PeerId}");
                }
            }

            snapshotTime += (float)delta;
            if (snapshotTime >= SnapshotInterval)
            {
                snapshotTime %= SnapshotInterval;
                foreach (PlayerRuntime player in players.Values)
                    Rpc(nameof(ReceivePosition), player.PeerId, player.Position.X, player.Position.Y);
            }

        }
        else
        {
            if (players.ContainsKey(localPeerId))
                RpcId(1, nameof(SubmitMovementInput), direction.X, direction.Y);

            foreach (PlayerRuntime player in players.Values)
            {
                if (player.PeerId == localPeerId) continue;
                player.Avatar.Position = player.Avatar.Position.Lerp(player.TargetPosition,
                    Mathf.Clamp((float)delta * 14f, 0f, 1f));
            }
        }
    }

    public override void _ExitTree()
    {
        if (GetMultiplayer().MultiplayerPeer is ENetMultiplayerPeer peer)
            peer.Close();
    }

    private void StartHost()
    {
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(PlaytestLaunchOptions.Port, MaximumPlayers - 1);
        if (error != Error.Ok)
        {
            SetStatus($"Could not host on UDP port {PlaytestLaunchOptions.Port}: {error}");
            return;
        }

        GetMultiplayer().MultiplayerPeer = peer;
        localPeerId = 1;
        hostAddress = GetLocalIpv4Address();
        SetStatus($"Pre-round · join {hostAddress}:{PlaytestLaunchOptions.Port}, then start the shift");
        startShiftButton.Visible = true;
        startShiftButton.Disabled = false;
        GD.Print($"PLAYTEST_HOST_READY port={PlaytestLaunchOptions.Port} phase={roundLifecycle.Phase}");
        RefreshStatus();
        if (PlaytestLaunchOptions.SmokeMove || PlaytestLaunchOptions.SmokeInteractions ||
            PlaytestLaunchOptions.SmokeConstruction || PlaytestLaunchOptions.SmokeEngineeringAccess)
            CallDeferred(nameof(StartShift));
    }

    private void JoinHost(string address)
    {
        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(address, PlaytestLaunchOptions.Port);
        if (error != Error.Ok)
        {
            SetStatus($"Could not connect to {address}:{PlaytestLaunchOptions.Port}: {error}");
            return;
        }

        GetMultiplayer().MultiplayerPeer = peer;
        SetStatus($"Connecting to {address}:{PlaytestLaunchOptions.Port}…");
        GD.Print($"PLAYTEST_JOINING address={address} port={PlaytestLaunchOptions.Port}");
    }

    private void OnPeerConnected(int peerId)
    {
        if (!GetMultiplayer().IsServer()) return;
        GD.Print($"PLAYTEST_PEER_CONNECTED peer={peerId}");
        RefreshStatus();
    }

    private void OnPeerDisconnected(int peerId)
    {
        if (!GetMultiplayer().IsServer()) return;
        if (players.Remove(peerId, out PlayerRuntime? player))
        {
            sharedInteractionWorld.RemoveActor(peerId);
            crewRoster.Release(peerId);
            collisionMap.RemoveObject(player.Obstacle);
            player.Avatar.QueueFree();
            Rpc(nameof(RemovePlayer), peerId);
        }
        GD.Print($"PLAYTEST_PEER_DISCONNECTED peer={peerId}");
        RefreshStatus();
    }

    private void OnConnectedToServer()
    {
        localPeerId = GetMultiplayer().GetUniqueId();
        SetStatus($"Connected · waiting for the host to start the shift (peer {localPeerId})");
        GD.Print($"PLAYTEST_CONNECTED local_peer={localPeerId}");
        RpcId(1, nameof(RequestRoundPhase));
    }

    private void StartShift()
    {
        if (!GetMultiplayer().IsServer()) return;
        if (!crewRoster.TryGetJob(PlaytestLaunchOptions.CrewJobId, out CrewJob? selectedJob) || selectedJob == null)
        {
            SetStatus("The selected crew role is unavailable. Return to the lobby and choose another role.");
            return;
        }
        RoundStartResult result = roundLifecycle.TryStart(localPeerId);
        if (result != RoundStartResult.Started) return;
        localRoundStarted = true;

        CrewAssignmentResult assignment = crewRoster.TryAssign(localPeerId, PlaytestLaunchOptions.CrewJobId);
        if (assignment != CrewAssignmentResult.Accepted ||
            !crewRoster.TryGetAssignment(localPeerId, out CrewJob? assignedJob) || assignedJob == null)
        {
            SetStatus($"Could not reserve the selected crew role: {assignment}.");
            return;
        }

        var spawn = StationPlaytestLayout.Spawns[0];
        Vector2 hostSpawn = PlaytestLaunchOptions.SmokeEngineeringAccess
            ? new Vector2(8f, -3f)
            : new Vector2(spawn.X, spawn.Y);
        AddPlayer(localPeerId, assignedJob, hostSpawn);
        startShiftButton.Disabled = true;
        startShiftButton.Text = "Shift started";
        SetStatus($"Shift started · joining crew can now spawn · {hostAddress}:{PlaytestLaunchOptions.Port}");
        GD.Print($"PLAYTEST_ROUND_STARTED host_job={assignedJob.Id}");
        Rpc(nameof(ReceiveRoundStarted));
        RefreshStatus();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestRoundPhase()
    {
        if (!GetMultiplayer().IsServer()) return;
        int peerId = (int)GetMultiplayer().GetRemoteSenderId();
        RpcId(peerId, nameof(ReceiveRoundPhase), (int)roundLifecycle.Phase);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundPhase(int phase)
    {
        if (phase == (int)SharedRoundPhase.Started)
        {
            localRoundStarted = true;
            RequestCrewSpawnFromClient();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundStarted()
    {
        localRoundStarted = true;
        RequestCrewSpawnFromClient();
    }

    private void RequestCrewSpawnFromClient()
    {
        if (GetMultiplayer().IsServer()) return;
        SetStatus("Shift started · requesting your crew role…");
        RpcId(1, nameof(RequestCrewSpawn), PlaytestLaunchOptions.CrewJobId);
    }

    private void OnConnectionFailed()
    {
        SetStatus($"Connection failed. Check the host address, UDP port {PlaytestLaunchOptions.Port}, and firewall settings.");
        GD.PrintErr("PLAYTEST_CONNECTION_FAILED");
    }

    private void OnServerDisconnected()
    {
        SetStatus("The host disconnected. Return to the lobby and reconnect.");
        GD.PrintErr("PLAYTEST_SERVER_DISCONNECTED");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestCrewSpawn(int jobId)
    {
        if (!GetMultiplayer().IsServer()) return;
        int peerId = (int)GetMultiplayer().GetRemoteSenderId();
        if (peerId <= 1) return;
        if (!roundLifecycle.CanRequestJob)
        {
            RpcId(peerId, nameof(RejectCrewRequest), jobId, (int)CrewAssignmentResult.RoundNotStarted);
            return;
        }
        if (players.TryGetValue(peerId, out PlayerRuntime? existingPlayer))
        {
            RpcId(peerId, nameof(ReceiveSpawn), existingPlayer.PeerId, existingPlayer.Job.Id,
                existingPlayer.Position.X, existingPlayer.Position.Y);
            return;
        }
        if (players.Count >= MaximumPlayers || players.Count >= StationPlaytestLayout.Spawns.Count)
        {
            RpcId(peerId, nameof(RejectCrewRequest), jobId, (int)CrewAssignmentResult.PositionFilled);
            return;
        }

        CrewAssignmentResult assignment = crewRoster.TryAssign(peerId, jobId);
        if (assignment != CrewAssignmentResult.Accepted ||
            !crewRoster.TryGetAssignment(peerId, out CrewJob? assignedJob) || assignedJob == null)
        {
            RpcId(peerId, nameof(RejectCrewRequest), jobId, (int)assignment);
            return;
        }

        var spawnPoint = StationPlaytestLayout.Spawns[players.Count];
        Vector2 spawn = PlaytestLaunchOptions.SmokeEngineeringAccess
            ? new Vector2(8f, -5f)
            : PlaytestLaunchOptions.SmokeConstruction
            ? new Vector2(1f, 1f)
            : PlaytestLaunchOptions.SmokeInteractions
            ? new Vector2(-1f, 1f)
            : new Vector2(spawnPoint.X, spawnPoint.Y);
        foreach (PlayerRuntime existing in players.Values)
            RpcId(peerId, nameof(ReceiveSpawn), existing.PeerId, existing.Job.Id,
                existing.Position.X, existing.Position.Y);

        AddPlayer(peerId, assignedJob, spawn);
        Rpc(nameof(ReceiveSpawn), peerId, assignedJob.Id, spawn.X, spawn.Y);
        SendSharedState(peerId);
        SendEngineeringAirlockState(peerId);
        SendConstructionState(peerId);
        SendActiveHand(peerId);
        GD.Print($"PLAYTEST_ROSTER peers={players.Count}");
        RefreshStatus();
    }

    private void SubmitSpeech(string message)
    {
        if (!localRoundStarted || !players.ContainsKey(localPeerId))
        {
            SetStatus("You can speak after joining the started shift.");
            return;
        }
        string text = message.Trim();
        if (text.Length == 0 || text.Length > 256 || text.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            SetStatus("Speech must be 1–256 characters on one line.");
            return;
        }

        if (GetMultiplayer().IsServer())
            RouteLocalSpeech(localPeerId, text);
        else
            RpcId(1, nameof(RequestLocalSpeech), text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestLocalSpeech(string message)
    {
        if (!GetMultiplayer().IsServer()) return;
        RouteLocalSpeech((int)GetMultiplayer().GetRemoteSenderId(), message);
    }

    private void RouteLocalSpeech(int speakerPeerId, string message)
    {
        if (!roundLifecycle.CanRequestJob || message.Length == 0 || message.Length > 256 ||
            message.IndexOfAny(['\r', '\n', '\0']) >= 0 || !players.TryGetValue(speakerPeerId, out PlayerRuntime? speaker))
            return;

        var listeners = new List<LocalChatListener>(players.Count);
        foreach (PlayerRuntime player in players.Values)
            listeners.Add(new LocalChatListener(player.PeerId,
                new System.Numerics.Vector2(player.Position.X, player.Position.Y),
                LocalSpeechRouter.DefaultHearingRange));

        System.Numerics.Vector2 origin = new(speaker.Position.X, speaker.Position.Y);
        IReadOnlyList<int> recipients = LocalSpeechRouter.FindListeners(speakerPeerId, origin, listeners,
            HasLocalLineOfSight);
        foreach (int recipient in recipients)
        {
            if (recipient == 1)
                AppendSpeech(speakerPeerId, speaker.Job.Name, message);
            else
                RpcId(recipient, nameof(ReceiveLocalSpeech), speakerPeerId, speaker.Job.Name, message);
        }

        GD.Print($"PLAYTEST_LOCAL_CHAT_RELAYED sender={speakerPeerId} recipients={recipients.Count}");
    }

    private bool HasLocalLineOfSight(System.Numerics.Vector2 from, System.Numerics.Vector2 to)
    {
        var movementState = new PlayerMovementState();
        return GridLineOfSight.IsClear(from, to, (x, y) => !collisionMap.IsPassable(x, y, movementState));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLocalSpeech(int speakerPeerId, string jobName, string message)
    {
        AppendSpeech(speakerPeerId, jobName, message);
    }

    private void AppendSpeech(int speakerPeerId, string jobName, string message)
    {
        chatLog.AppendText($"[LOCAL] Crew {speakerPeerId} · {jobName}: {message}\n");
        chatLog.ScrollToLine(Math.Max(0, chatLog.GetLineCount() - 1));
        GD.Print($"PLAYTEST_LOCAL_CHAT_RECEIVED speaker={speakerPeerId}");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SubmitMovementInput(int deltaX, int deltaY)
    {
        if (!GetMultiplayer().IsServer()) return;
        int senderId = (int)GetMultiplayer().GetRemoteSenderId();
        SetMovementInput(senderId, senderId, deltaX, deltaY);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSpawn(int peerId, int jobId, float x, float y)
    {
        if (players.ContainsKey(peerId)) return;
        if (!StationCrewJobCatalog.CreateRoster().TryGetJob(jobId, out CrewJob? job) || job == null)
        {
            GD.PrintErr($"PLAYTEST_INVALID_SERVER_JOB peer={peerId} job={jobId}");
            return;
        }
        AddPlayer(peerId, job, new Vector2(x, y));
        GD.Print($"PLAYTEST_PLAYER_SPAWNED peer={peerId} job={job.Id} role={job.Name} count={players.Count}");
        RefreshStatus();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectCrewRequest(int jobId, int resultCode)
    {
        var result = (CrewAssignmentResult)resultCode;
        string jobName = StationCrewJobCatalog.CreateRoster().TryGetJob(jobId, out CrewJob? job) && job != null
            ? job.Name
            : $"job {jobId}";
        string reason = result switch
        {
            CrewAssignmentResult.PositionFilled => "has no positions left",
            CrewAssignmentResult.UnknownJob => "is not available in this playtest",
            CrewAssignmentResult.InvalidPeer => "could not be assigned to your connection",
            CrewAssignmentResult.RoundNotStarted => "cannot be requested until the host starts the shift",
            _ => "could not be assigned",
        };
        SetStatus($"{jobName} {reason}. Return to the lobby and choose another role.");
        GD.PrintErr($"PLAYTEST_CREW_REQUEST_REJECTED job={jobId} result={result}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemovePlayer(int peerId)
    {
        if (!players.Remove(peerId, out PlayerRuntime? player)) return;
        collisionMap.RemoveObject(player.Obstacle);
        player.Avatar.QueueFree();
        RefreshStatus();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceivePosition(int peerId, float x, float y)
    {
        if (!players.TryGetValue(peerId, out PlayerRuntime? player)) return;
        player.TargetPosition = new Vector2(x, y);
        if (peerId == localPeerId)
            player.Avatar.Position = player.TargetPosition;
        else if (!player.HasLoggedRemoteMovement && player.TargetPosition.DistanceSquaredTo(player.SpawnPosition) > 0.08f)
        {
            player.HasLoggedRemoteMovement = true;
            GD.Print($"PLAYTEST_REMOTE_MOVE peer={peerId}");
        }
    }

    private void AddPlayer(int peerId, CrewJob job, Vector2 position)
    {
        if (players.ContainsKey(peerId)) return;

        var avatar = new StationCrewAvatar { Name = $"Crew_{peerId}", Position = position };
        avatar.Configure(peerId, peerId == GetMultiplayer().GetUniqueId(),
            peerId == 1 ? "CREW · HOST" : $"CREW · {players.Count + 1}", job.Name);
        AddChild(avatar);

        var obstacle = new DynamicObject(Mathf.RoundToInt(position.X), Mathf.RoundToInt(position.Y),
            isPushable: false, tag: "player");
        collisionMap.AddObject(obstacle);
        players.Add(peerId, new PlayerRuntime(peerId, job, avatar, position, obstacle));
        sharedInteractionWorld.AddActor(peerId, new System.Numerics.Vector2(position.X, position.Y), job.Id);
        if (peerId == GetMultiplayer().GetUniqueId())
            UpdateLoadoutLabel(sharedInteractionWorld.GetCrewLoadoutSummary(peerId));
    }

    private void SetMovementInput(int ownerPeerId, int senderPeerId, int deltaX, int deltaY)
    {
        if (!MovementCommandAuthorization.IsValid(ownerPeerId, senderPeerId, deltaX, deltaY))
            return;
        if (!players.TryGetValue(ownerPeerId, out PlayerRuntime? player)) return;
        player.DeltaX = deltaX;
        player.DeltaY = deltaY;
    }

    private Vector2I ReadLocalInput()
    {
        if (PlaytestLaunchOptions.SmokeMove)
        {
            if (players.Count >= 2 && !smokeMoveStarted)
            {
                smokeMoveStarted = true;
                smokeMoveUntilMsec = Time.GetTicksMsec() + 1500;
            }
            if (smokeMoveStarted && Time.GetTicksMsec() < smokeMoveUntilMsec)
                return Vector2I.Right;
        }

        Vector2 direction = Godot.Input.GetVector("move_left", "move_right", "move_up", "move_down");
        return new Vector2I(Mathf.Sign(direction.X), Mathf.Sign(direction.Y));
    }

    private void BuildStationView()
    {
        EnsureMovementActions();
        stationMapView = new StationMapView();
        stationMapView.SetSharedObjects(wrenchOwner, wrenchWorldPosition, fixtureWorldPosition, fixtureAnchored);
        stationMapView.SetEngineeringAirlock(engineeringAirlockOpen);
        AddChild(stationMapView);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab } &&
            localRoundStarted && players.ContainsKey(GetMultiplayer().GetUniqueId()))
        {
            if (GetMultiplayer().IsServer())
                ResolveActiveHandToggle(localPeerId);
            else
                RpcId(1, nameof(RequestActiveHandToggle));
            return;
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q } &&
            localRoundStarted && players.ContainsKey(GetMultiplayer().GetUniqueId()))
        {
            if (GetMultiplayer().IsServer())
                ResolveSharedDrop(localPeerId);
            else
                RpcId(1, nameof(RequestSharedDrop));
            return;
        }

        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click ||
            !localRoundStarted || !players.ContainsKey(GetMultiplayer().GetUniqueId())) return;

        Vector2 clickPosition = GetGlobalMousePosition();
        int targetId = -1;
        float nearestDistance = 0.7f;
        float airlockDistance = clickPosition.DistanceTo(new Vector2(8, -4));
        if (airlockDistance < nearestDistance)
        {
            targetId = SharedStationInventoryWorld.EngineeringAirlockId;
            nearestDistance = airlockDistance;
        }
        if (wrenchOwner == null)
        {
            float distance = clickPosition.DistanceTo(wrenchWorldPosition);
            if (distance < nearestDistance) { targetId = SharedStationInventoryWorld.WrenchId; nearestDistance = distance; }
        }
        float metalDistance = clickPosition.DistanceTo(new Vector2(1, 2));
        SharedStationInventoryWorld.StationConstructionSnapshot construction = sharedInteractionWorld.ConstructionSnapshot;
        if (construction.MetalOwner == null && construction.MetalSheetCount > 0 && metalDistance < nearestDistance)
        {
            targetId = SharedStationInventoryWorld.MetalSheetsId;
            nearestDistance = metalDistance;
        }
        float girderDistance = clickPosition.DistanceTo(new Vector2(2, 2));
        if (!sharedInteractionWorld.ConstructionSnapshot.GirderPlated && girderDistance < nearestDistance)
        {
            targetId = SharedStationInventoryWorld.GirderId;
            nearestDistance = girderDistance;
        }
        float fixtureDistance = clickPosition.DistanceTo(fixtureWorldPosition);
        if (fixtureDistance < nearestDistance) targetId = SharedStationInventoryWorld.FixtureId;
        if (targetId < 0) return;

        bool altClick = click.AltPressed;
        if (GetMultiplayer().IsServer())
            ResolveSharedInteraction(localPeerId, targetId, (int)Intent.Help, altClick);
        else
            RpcId(1, nameof(RequestSharedInteraction), targetId, (int)Intent.Help, altClick);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSharedInteraction(int targetId, int intentValue, bool altClick)
    {
        if (!GetMultiplayer().IsServer()) return;
        int senderId = (int)GetMultiplayer().GetRemoteSenderId();
        ResolveSharedInteraction(senderId, targetId, intentValue, altClick);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSharedDrop()
    {
        if (!GetMultiplayer().IsServer()) return;
        ResolveSharedDrop((int)GetMultiplayer().GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestActiveHandToggle()
    {
        if (!GetMultiplayer().IsServer()) return;
        ResolveActiveHandToggle((int)GetMultiplayer().GetRemoteSenderId());
    }

    private void ResolveActiveHandToggle(int senderId)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId) ||
            !sharedInteractionWorld.TryToggleActiveHand(senderId)) return;
        if (senderId == 1)
            UpdateActiveHandLabel(sharedInteractionWorld.GetActiveHand(senderId));
        else
            SendActiveHand(senderId);
    }

    private void SendActiveHand(int peerId)
    {
        var hand = sharedInteractionWorld.GetActiveHand(peerId);
        if (hand == null) return;
        string itemName = sharedInteractionWorld.GetHandItemName(peerId, hand.Value);
        if (peerId == 1)
            UpdateActiveHandLabel(hand, itemName);
        else
            RpcId(peerId, nameof(ReceiveActiveHand), (int)hand.Value, itemName);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveActiveHand(int handValue, string itemName) =>
        UpdateActiveHandLabel((Godotstation.Engine.Inventory.NamedSlot)handValue, itemName);

    private void UpdateActiveHandLabel(Godotstation.Engine.Inventory.NamedSlot? hand, string? itemName = null)
    {
        if (activeHandLabel == null || hand == null) return;
        itemName ??= sharedInteractionWorld.GetHandItemName(localPeerId, hand.Value);
        activeHandLabel.Text = $"Active hand: {hand.Value} · {(itemName.Length == 0 ? "empty" : itemName)}";
    }

    private void UpdateLoadoutLabel(string summary)
    {
        if (IsInstanceValid(loadoutLabel)) loadoutLabel.Text = summary;
    }

    private void ResolveSharedDrop(int senderId)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId)) return;
        if (sharedInteractionWorld.TryDropActiveHand(senderId))
            SendActiveHand(senderId);
    }

    private void DriveSmokeInteractions()
    {
        if (!PlaytestLaunchOptions.SmokeInteractions || GetMultiplayer().IsServer() ||
            smokeInteractionsCompleted || !localRoundStarted || players.Count < 2) return;

        if (!smokeInteractionPickupRequested)
        {
            smokeInteractionPickupRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), SharedStationInventoryWorld.WrenchId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_INTERACTION_REQUEST pickup");
            return;
        }

        if (wrenchOwner == localPeerId && !smokeInteractionUseRequested)
        {
            smokeInteractionUseRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), SharedStationInventoryWorld.FixtureId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_INTERACTION_REQUEST wrench-use");
            return;
        }

        if (wrenchOwner == localPeerId && !fixtureAnchored && !smokeInteractionDropRequested)
        {
            smokeInteractionDropRequested = true;
            RpcId(1, nameof(RequestSharedDrop));
            GD.Print("PLAYTEST_INTERACTION_REQUEST drop");
            return;
        }

        if (smokeInteractionDropRequested && wrenchOwner == null && !fixtureAnchored)
        {
            smokeInteractionsCompleted = true;
            GD.Print("PLAYTEST_SHARED_INTERACTION_OK pickup=true wrench_use=true dropped=true replicated=true");
        }
    }

    private void DriveSmokeConstruction()
    {
        if (!PlaytestLaunchOptions.SmokeConstruction || GetMultiplayer().IsServer() ||
            smokeConstructionCompleted || !localRoundStarted || players.Count < 2) return;

        if (!smokeConstructionPickupRequested)
        {
            smokeConstructionPickupRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), SharedStationInventoryWorld.MetalSheetsId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_CONSTRUCTION_REQUEST pickup-metal");
            return;
        }

        if (smokeMetalOwner == localPeerId && !smokeConstructionStarted)
        {
            smokeConstructionStarted = true;
            RpcId(1, nameof(RequestSharedInteraction), SharedStationInventoryWorld.GirderId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_CONSTRUCTION_REQUEST plate-girder");
            return;
        }

        if (smokeGirderPlated && smokeSheetCount == 0)
        {
            smokeConstructionCompleted = true;
            GD.Print("PLAYTEST_GIRDER_PLATING_OK engineer=true sheets_consumed=2 duration_seconds=4 replicated=true");
        }
    }

    private void DriveSmokeEngineeringAccess()
    {
        if (!PlaytestLaunchOptions.SmokeEngineeringAccess || !localRoundStarted || players.Count < 2 ||
            smokeEngineeringAccessVerified) return;

        if (GetMultiplayer().IsServer())
        {
            if (smokeEngineeringAccessDenied && !smokeEngineerDoorRequestSent)
            {
                smokeEngineerDoorRequestSent = true;
                ResolveSharedInteraction(localPeerId, SharedStationInventoryWorld.EngineeringAirlockId,
                    (int)Intent.Help, false);
            }
            return;
        }

        if (!smokeEngineeringAccessRequested)
        {
            smokeEngineeringAccessRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), SharedStationInventoryWorld.EngineeringAirlockId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_ENGINEERING_ACCESS_REQUEST assistant_try");
        }
    }

    private void ResolveSharedInteraction(int senderId, int targetId, int intentValue, bool altClick)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId) ||
            intentValue is < (int)Intent.Help or > (int)Intent.Harm) return;
        if (!sharedInteractionWorld.TryInteract(senderId, targetId, (Intent)intentValue, altClick))
        {
            bool accessDenied = targetId == SharedStationInventoryWorld.EngineeringAirlockId &&
                !sharedInteractionWorld.CanCrewOpenEngineeringAirlock(senderId);
            if (GetMultiplayer().IsServer())
                GD.Print($"PLAYTEST_INTERACTION_REJECTED peer={senderId} target={targetId} active_hand={sharedInteractionWorld.GetHandItemName(senderId, sharedInteractionWorld.GetActiveHand(senderId) ?? Godotstation.Engine.Inventory.NamedSlot.rightHand)}");
            if (accessDenied)
            {
                if (GetMultiplayer().IsServer())
                    GD.Print($"PLAYTEST_ENGINEERING_ACCESS_DENIED peer={senderId} required=Engine");
                if (PlaytestLaunchOptions.SmokeEngineeringAccess && senderId != 1)
                    smokeEngineeringAccessDenied = true;
                if (senderId == localPeerId) ReceiveEngineeringAccessDenied();
                else RpcId(senderId, nameof(ReceiveEngineeringAccessDenied));
            }
            else if (senderId == localPeerId) SetStatus(targetId == SharedStationInventoryWorld.GirderId
                    ? "Girder plating requires two metal sheets in your active hand and a clear reach to the girder."
                    : "Too far away, blocked, or you need the wrench in hand.");
            else RpcId(senderId, nameof(RejectSharedInteraction));
        }
        else
        {
            if (GetMultiplayer().IsServer() && targetId == SharedStationInventoryWorld.GirderId)
                GD.Print($"PLAYTEST_GIRDER_PLATING_STARTED peer={senderId}");
            SendActiveHand(senderId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectSharedInteraction() => SetStatus("Too far away, blocked, or you need the wrench in hand.");

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEngineeringAccessDenied()
    {
        smokeEngineeringAccessDenied = true;
        SetStatus("Engineering airlock denied · Engineer ID clearance required.");
        GD.Print("PLAYTEST_ENGINEERING_ACCESS_DENIED_RECEIVED");
    }

    private void OnSharedStateChanged(SharedStationWorldSnapshot snapshot)
    {
        wrenchOwner = snapshot.WrenchOwner;
        wrenchWorldPosition = new Vector2(snapshot.WrenchPosition.X, snapshot.WrenchPosition.Y);
        fixtureWorldPosition = new Vector2(snapshot.FixturePosition.X, snapshot.FixturePosition.Y);
        fixtureAnchored = snapshot.FixtureAnchored;
        fixtureObstacle.IsPushable = !fixtureAnchored;
        fixtureObstacle.X = Mathf.RoundToInt(fixtureWorldPosition.X);
        fixtureObstacle.Y = Mathf.RoundToInt(fixtureWorldPosition.Y);
        stationMapView?.SetSharedObjects(wrenchOwner, wrenchWorldPosition, fixtureWorldPosition, fixtureAnchored);
        if (!GetMultiplayer().IsServer()) return;
        foreach (PlayerRuntime player in players.Values)
            SendActiveHand(player.PeerId);
        if (PlaytestLaunchOptions.SmokeInteractions)
            GD.Print($"PLAYTEST_INTERACTION_SERVER_STATE owner={wrenchOwner ?? 0} anchored={fixtureAnchored}");
        Rpc(nameof(ReceiveSharedState), wrenchOwner ?? 0, wrenchWorldPosition.X, wrenchWorldPosition.Y,
            fixtureWorldPosition.X, fixtureWorldPosition.Y, fixtureAnchored);
        PublishConstructionStateToClients();
    }

    private void OnConstructionStateChanged(SharedStationInventoryWorld.StationConstructionSnapshot snapshot)
    {
        ApplyConstructionState(snapshot);
        if (snapshot.GirderPlated)
            GD.Print("PLAYTEST_GIRDER_PLATED host_state=true metal_sheets_remaining=0");
        PublishConstructionStateToClients();
    }

    private void OnEngineeringAirlockStateChanged(bool isOpen)
    {
        engineeringAirlockOpen = isOpen;
        stationMapView?.SetEngineeringAirlock(isOpen);
        if (!GetMultiplayer().IsServer()) return;
        if (isOpen) GD.Print("PLAYTEST_ENGINEERING_AIRLOCK_OPENED peer=1 clearance=Engine");
        Rpc(nameof(ReceiveEngineeringAirlockState), isOpen);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEngineeringAirlockState(bool isOpen)
    {
        engineeringAirlockOpen = isOpen;
        stationMapView?.SetEngineeringAirlock(isOpen);
        if (!GetMultiplayer().IsServer() && PlaytestLaunchOptions.SmokeEngineeringAccess &&
            smokeEngineeringAccessDenied && isOpen && !smokeEngineeringAccessVerified)
        {
            smokeEngineeringAccessVerified = true;
            GD.Print("PLAYTEST_ENGINEERING_ACCESS_OK denied=true engineer_open=true replicated=true");
        }
    }

    private void SendEngineeringAirlockState(int peerId) =>
        RpcId(peerId, nameof(ReceiveEngineeringAirlockState), sharedInteractionWorld.IsEngineeringAirlockOpen);

    private void ApplyConstructionState(SharedStationInventoryWorld.StationConstructionSnapshot snapshot) =>
        SetConstructionView(snapshot.MetalOwner,
            new Vector2(snapshot.MetalPosition.X, snapshot.MetalPosition.Y), snapshot.MetalSheetCount,
            new Vector2(snapshot.GirderPosition.X, snapshot.GirderPosition.Y), snapshot.GirderPlated,
            snapshot.WorkerPeerId, snapshot.ProgressSeconds);

    private void SetConstructionView(int? metalOwner, Vector2 metalPosition, int sheetCount,
        Vector2 girderPosition, bool plated, int? workerPeerId, double progressSeconds)
    {
        smokeMetalOwner = metalOwner ?? 0;
        smokeSheetCount = sheetCount;
        smokeGirderPlated = plated;
        stationMapView.SetConstruction(metalOwner, metalPosition, sheetCount, girderPosition,
            plated, workerPeerId, progressSeconds);
    }

    private void PublishConstructionStateToClients()
    {
        if (!GetMultiplayer().IsServer()) return;
        SharedStationInventoryWorld.StationConstructionSnapshot snapshot = sharedInteractionWorld.ConstructionSnapshot;
        Rpc(nameof(ReceiveConstructionState), snapshot.MetalOwner ?? 0,
            snapshot.MetalPosition.X, snapshot.MetalPosition.Y, snapshot.MetalSheetCount,
            snapshot.GirderPosition.X, snapshot.GirderPosition.Y, snapshot.GirderPlated,
            snapshot.WorkerPeerId ?? 0, snapshot.ProgressSeconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveConstructionState(int ownerPeerId, float metalX, float metalY, int sheetCount,
        float girderX, float girderY, bool plated, int workerPeerId, double progressSeconds)
    {
        SetConstructionView(ownerPeerId == 0 ? null : ownerPeerId, new Vector2(metalX, metalY), sheetCount,
            new Vector2(girderX, girderY), plated, workerPeerId == 0 ? null : workerPeerId, progressSeconds);
        if (plated && GetMultiplayer().IsServer())
            GD.Print("PLAYTEST_GIRDER_PLATED host_state=true metal_sheets_remaining=0");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSharedState(int ownerPeerId, float wrenchX, float wrenchY, float fixtureX, float fixtureY, bool anchored)
    {
        wrenchOwner = ownerPeerId == 0 ? null : ownerPeerId;
        wrenchWorldPosition = new Vector2(wrenchX, wrenchY);
        fixtureWorldPosition = new Vector2(fixtureX, fixtureY);
        fixtureAnchored = anchored;
        stationMapView.SetSharedObjects(wrenchOwner, wrenchWorldPosition, fixtureWorldPosition, fixtureAnchored);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveInitialSharedState(int ownerPeerId, float wrenchX, float wrenchY, float fixtureX, float fixtureY, bool anchored)
        => ReceiveSharedState(ownerPeerId, wrenchX, wrenchY, fixtureX, fixtureY, anchored);

    private void SendSharedState(int peerId)
    {
        SharedStationWorldSnapshot snapshot = sharedInteractionWorld.Snapshot;
        RpcId(peerId, nameof(ReceiveInitialSharedState), snapshot.WrenchOwner ?? 0,
            snapshot.WrenchPosition.X, snapshot.WrenchPosition.Y,
            snapshot.FixturePosition.X, snapshot.FixturePosition.Y, snapshot.FixtureAnchored);
    }

    private void SendConstructionState(int peerId)
    {
        SharedStationInventoryWorld.StationConstructionSnapshot snapshot = sharedInteractionWorld.ConstructionSnapshot;
        RpcId(peerId, nameof(ReceiveConstructionState), snapshot.MetalOwner ?? 0,
            snapshot.MetalPosition.X, snapshot.MetalPosition.Y, snapshot.MetalSheetCount,
            snapshot.GirderPosition.X, snapshot.GirderPosition.Y, snapshot.GirderPlated,
            snapshot.WorkerPeerId ?? 0, snapshot.ProgressSeconds);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "PlaytestHud" };
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(14, 14) };
        layer.AddChild(panel);
        var content = new VBoxContainer();
        panel.AddChild(content);
        var title = new Label { Text = "GODOTSTATION · CREW TEST" };
        title.AddThemeFontSizeOverride("font_size", 20);
        content.AddChild(title);
        statusLabel = new Label { Text = connectionStatus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        statusLabel.CustomMinimumSize = new Vector2(420, 34);
        content.AddChild(statusLabel);
        startShiftButton = new Button { Text = "Start shift", Visible = false, Disabled = true };
        startShiftButton.Pressed += StartShift;
        content.AddChild(startShiftButton);
        content.AddChild(new Label { Text = $"WASD move  ·  Host shares LAN IP  ·  UDP {PlaytestLaunchOptions.Port}" });
        activeHandLabel = new Label { Text = "Active hand: rightHand · empty" };
        content.AddChild(activeHandLabel);
        loadoutLabel = new Label { Text = "Crew loadout: awaiting role spawn", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        loadoutLabel.CustomMinimumSize = new Vector2(430, 60);
        content.AddChild(loadoutLabel);
        content.AddChild(new Label { Text = "Engineer task: pick up the metal, click the girder to plate it (4 seconds), and watch the wall appear. Click the wrench/cart to test tools; Tab switches hands, Q drops the active hand, WASD moves." });

        chatLog = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(440, 150),
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
        };
        content.AddChild(chatLog);
        chatInput = new LineEdit
        {
            PlaceholderText = "Speak locally… (Enter)",
            CustomMinimumSize = new Vector2(440, 36),
            MaxLength = 256,
        };
        chatInput.TextSubmitted += SubmitSpeech;
        content.AddChild(chatInput);

        var returnButton = new Button { Text = "Return to lobby" };
        returnButton.Pressed += () => GetTree().ChangeSceneToFile("res://UI/RoundLobby.tscn");
        content.AddChild(returnButton);
    }

    private static string GetLocalIpv4Address()
    {
        foreach (string address in IP.GetLocalAddresses())
        {
            if (IPAddress.TryParse(address, out IPAddress? parsed) &&
                parsed.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(parsed))
                return address;
        }

        return "LAN IP unavailable";
    }

    private void EnsureMovementActions()
    {
        AddMovementAction("move_left", Key.A);
        AddMovementAction("move_right", Key.D);
        AddMovementAction("move_up", Key.W);
        AddMovementAction("move_down", Key.S);
    }

    private static void AddMovementAction(string action, Key key)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        var keyEvent = new InputEventKey { PhysicalKeycode = key };
        InputMap.ActionAddEvent(action, keyEvent);
    }

    private void SetStatus(string value)
    {
        connectionStatus = value;
        if (IsInstanceValid(statusLabel)) statusLabel.Text = value;
    }

    private void RefreshStatus()
    {
        if (GetMultiplayer().IsServer())
        {
            string phase = roundLifecycle.Phase == SharedRoundPhase.PreRound ? "Pre-round" : "Shift started";
            string hostAction = roundLifecycle.Phase == SharedRoundPhase.PreRound ? " · press Start shift when ready" : string.Empty;
            connectionStatus = $"{phase} · join {hostAddress}:{PlaytestLaunchOptions.Port} · crew {players.Count}/{MaximumPlayers}{hostAction}";
        }
        else if (players.Count > 0)
            connectionStatus = $"Connected · crew {players.Count}/{MaximumPlayers}";

        if (IsInstanceValid(statusLabel)) statusLabel.Text = connectionStatus;
    }

    private sealed class PlayerRuntime(int peerId, CrewJob job, Node2D avatar, Vector2 spawnPosition, DynamicObject obstacle)
    {
        public int PeerId { get; } = peerId;
        public CrewJob Job { get; } = job;
        public Node2D Avatar { get; } = avatar;
        public Vector2 SpawnPosition { get; } = spawnPosition;
        public Vector2 Position { get; set; } = spawnPosition;
        public Vector2 TargetPosition { get; set; } = spawnPosition;
        public DynamicObject Obstacle { get; } = obstacle;
        public PlayerMovementState Movement { get; } = new();
        public int DeltaX { get; set; }
        public int DeltaY { get; set; }
        public bool HasLoggedMovement { get; set; }
        public bool HasLoggedRemoteMovement { get; set; }
    }

    private sealed class StationReachabilityMap(CollisionMap map) : IReachabilityMap
    {
        public bool IsPassableForClick(System.Numerics.Vector2 fromWorldPos,
            System.Numerics.Vector2 toWorldPos, bool isServer, WorldEntity? context = null)
        {
            var movementState = new PlayerMovementState();
            return GridLineOfSight.IsClear(fromWorldPos, toWorldPos,
                (x, y) => !map.IsPassable(x, y, movementState));
        }
    }
}
