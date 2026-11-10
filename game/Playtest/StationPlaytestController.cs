using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
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
    private CrewRoster crewRoster = StationCrewJobCatalog.CreateRoster();
    private readonly SharedRoundLifecycle roundLifecycle = new();
    private CollisionMap collisionMap = null!;
    private MovementProcessor movementProcessor = null!;
    private SharedStationInventoryWorld sharedInteractionWorld = null!;
    private ReplicatedStationEntities replicatedEntities = new();
    private StationMapView stationMapView = null!;
    private readonly Dictionary<int, DynamicObject> entityObstacles = new();
    private Label statusLabel = null!;
    private Label activeHandLabel = null!;
    private Label loadoutLabel = null!;
    private Label healthLabel = null!;
    private Label intentLabel = null!;
    private Intent selectedIntent = Intent.Help;
    private Button startShiftButton = null!;
    private Button endShiftButton = null!;
    private Button restartShiftButton = null!;
    private AcceptDialog roundEndReportDialog = null!;
    private RichTextLabel chatLog = null!;
    private LineEdit chatInput = null!;
    private int localPeerId;
    private int localRoundId = 1;
    private ulong shiftStartedAtMsec;
    private RoundEndReport? currentRoundEndReport;
    private float snapshotTime;
    private float entitySnapshotTime;
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
    private int smokeRoundRestartStage;
    private ulong smokeRoundRestartAfterMsec;
    private bool smokeRoundRestartClientVerified;
    private int smokePowerStage;
    private bool smokePowerSawOn;
    private bool smokePowerSawOutage;
    private bool smokePowerSawRecovery;
    private bool smokePowerCompleted;
    private bool smokePowerHostSawOn;
    private bool smokePowerHostSawOutage;
    private int smokeHealthStage;
    private ulong smokeHealthNextAttackMsec;
    private bool smokeHealthHostSawCrit;
    private bool smokeHealthHostBlocked;
    private bool smokeHealthHostSawRecovery;
    private int smokeGhostStage;
    private ulong smokeGhostNextAttackMsec;
    private bool smokeGhostMoving;
    private bool smokeGhostCrossedWall;
    private bool smokeGhostHeardLiving;
    private int smokeExamineStage;
    private int smokeExamineReceived;
    private bool smokeExamineRejected;
    private int smokePullStage;
    private Vector2I smokePullInput;
    private Label pullLabel = null!;
    private string activeHandItemName = string.Empty;
    private bool localRoundStarted;
    private string connectionStatus = "Connecting…";
    private string hostAddress = "LAN IP unavailable";

    public override void _Ready()
    {
        SetMultiplayerAuthority(1);
        InitializeWorld();
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

    private void InitializeWorld()
    {
        collisionMap = StationPlaytestLayout.CreateCollisionMap();
        movementProcessor = new MovementProcessor(collisionMap);
        sharedInteractionWorld = new SharedStationInventoryWorld(new StationReachabilityMap(collisionMap));
        sharedInteractionWorld.EntitiesChanged += OnEntitiesChanged;
        sharedInteractionWorld.CrewAttacked += OnCrewAttacked;
        sharedInteractionWorld.CrewTreated += OnCrewTreated;
        sharedInteractionWorld.CrewStatChanged += OnCrewStatChanged;
        sharedInteractionWorld.CrewGhosted += OnCrewGhosted;
        sharedInteractionWorld.PullStarted += OnPullStarted;
        sharedInteractionWorld.CprStarted += OnCprStarted;
        sharedInteractionWorld.CrewStruckUnarmed += OnCrewStruckUnarmed;
        sharedInteractionWorld.CprPerformed += OnCprPerformed;
        sharedInteractionWorld.PullStopped += OnPullStopped;
        foreach ((int x, int y, IPassable passable) in sharedInteractionWorld.PassableTiles)
            collisionMap.SetTile(x, y, passable);
        entityObstacles.Clear();
        replicatedEntities = new ReplicatedStationEntities(sharedInteractionWorld.CaptureAll());
        SyncEntityObstacles(replicatedEntities.All);
        stationMapView?.SetEntities(replicatedEntities);
        snapshotTime = 0;
        entitySnapshotTime = 0;
    }

    private void ResetSharedWorld()
    {
        foreach (PlayerRuntime player in players.Values)
            player.Avatar.QueueFree();
        players.Clear();
        sharedInteractionWorld.EntitiesChanged -= OnEntitiesChanged;
        sharedInteractionWorld.CrewAttacked -= OnCrewAttacked;
        sharedInteractionWorld.CrewTreated -= OnCrewTreated;
        sharedInteractionWorld.CrewStatChanged -= OnCrewStatChanged;
        sharedInteractionWorld.CrewGhosted -= OnCrewGhosted;
        sharedInteractionWorld.PullStarted -= OnPullStarted;
        sharedInteractionWorld.CprStarted -= OnCprStarted;
        sharedInteractionWorld.CrewStruckUnarmed -= OnCrewStruckUnarmed;
        sharedInteractionWorld.CprPerformed -= OnCprPerformed;
        sharedInteractionWorld.PullStopped -= OnPullStopped;
        crewRoster = StationCrewJobCatalog.CreateRoster();
        InitializeWorld();
        localRoundStarted = false;
        currentRoundEndReport = null;
        shiftStartedAtMsec = 0;
        if (IsInstanceValid(roundEndReportDialog)) roundEndReportDialog.Hide();
        activeHandLabel.Text = "Active hand: rightHand · empty";
        loadoutLabel.Text = "Crew loadout: awaiting role spawn";
        healthLabel.Text = "Health: awaiting role spawn";
        chatLog.Clear();
    }

    public override void _PhysicsProcess(double delta)
    {
        var multiplayer = GetMultiplayer();
        if (multiplayer.MultiplayerPeer == null || multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
            return;

        localPeerId = multiplayer.GetUniqueId();
        DriveSmokeRoundRestart();
        if (!localRoundStarted) return;
        if (PlaytestLaunchOptions.SmokeChat && !smokeChatStarted && players.Count >= 2 && localRoundStarted)
        {
            smokeChatStarted = true;
            SubmitSpeech($"SMOKE_CHAT_PEER_{localPeerId}");
        }
        DriveSmokeInteractions();
        DriveSmokeConstruction();
        DriveSmokeEngineeringAccess();
        DriveSmokePowerLoop();
        DriveSmokeHealth();
        DriveSmokeGhost();
        DriveSmokeExamine();
        DriveSmokePull();
        var direction = ReadLocalInput();

        if (multiplayer.IsServer())
        {
            if (players.ContainsKey(localPeerId))
                SetMovementInput(localPeerId, localPeerId, direction.X, direction.Y);

            float step = (float)Math.Min(delta, 0.1);
            sharedInteractionWorld.AdvancePower(step);
            sharedInteractionWorld.AdvanceHealth(step);
            foreach (PlayerRuntime player in players.Values)
            {
                CrewStat stat = sharedInteractionWorld.GetCrewStat(player.PeerId) ?? CrewStat.Stable;
                int moveX = player.DeltaX;
                int moveY = player.DeltaY;
                if (stat == CrewStat.Dead)
                    sharedInteractionWorld.TryMoveGhost(player.PeerId, moveX, moveY, step, GhostMinimum, GhostMaximum);
                if (!stat.CanMove() || sharedInteractionWorld.IsImmobilizedByPull(player.PeerId))
                {
                    moveX = 0;
                    moveY = 0;
                }
                player.Movement.SpeedModifier = sharedInteractionWorld.IsDraggingProne(player.PeerId)
                    ? SharedStationInventoryWorld.ProneDragSpeedFactor
                    : 1f;
                player.Movement.CurrentType = stat.IsFloored() ? MovementType.Crawling : MovementType.Running;
                SyncCrewDensity(player, stat);
                float x = player.Position.X;
                float y = player.Position.Y;
                var intent = new MoveIntent
                {
                    DeltaX = moveX,
                    DeltaY = moveY,
                    Timestamp = Time.GetTicksMsec() / 1000.0
                };

                if (intent.DeltaX != 0 || intent.DeltaY != 0)
                    movementProcessor.TryMove(player.Movement, ref x, ref y, intent, step);
                else
                    movementProcessor.ApplyMomentum(player.Movement, ref x, ref y, step);

                if (player.ObstacleActive && (Mathf.RoundToInt(x) != Mathf.RoundToInt(player.Position.X) ||
                    Mathf.RoundToInt(y) != Mathf.RoundToInt(player.Position.Y)))
                    collisionMap.MoveObject(player.Obstacle, Mathf.RoundToInt(x), Mathf.RoundToInt(y));

                var vacatedTile = new System.Numerics.Vector2(Mathf.RoundToInt(player.Position.X),
                    Mathf.RoundToInt(player.Position.Y));
                player.Position = new Vector2(x, y);
                sharedInteractionWorld.SetActorPosition(player.PeerId, new System.Numerics.Vector2(x, y));
                if (sharedInteractionWorld.TryGetPullFollow(player.PeerId, vacatedTile, out int pulledId,
                        out System.Numerics.Vector2 pullDestination))
                    MovePulledEntity(pulledId, pullDestination);
                sharedInteractionWorld.AdvanceConstruction(player.PeerId, step);
                SyncPushedEntities();
                player.Avatar.Position = player.Position;
                if (!player.HasLoggedMovement && player.Position.DistanceSquaredTo(player.SpawnPosition) > 0.08f)
                {
                    player.HasLoggedMovement = true;
                    GD.Print($"PLAYTEST_POSITION peer={player.PeerId} x={x:0.00} y={y:0.00}");
                    if (player.PeerId != 1)
                        GD.Print($"PLAYTEST_CLIENT_POSITION peer={player.PeerId}");
                }
            }

            sharedInteractionWorld.ValidatePulls();
            entitySnapshotTime += step;
            if (entitySnapshotTime >= SnapshotInterval)
            {
                entitySnapshotTime %= SnapshotInterval;
                sharedInteractionWorld.PublishChanges();
            }

            snapshotTime += (float)delta;
            if (snapshotTime >= SnapshotInterval)
            {
                snapshotTime %= SnapshotInterval;
                foreach (PlayerRuntime player in players.Values)
                    Rpc(nameof(ReceivePosition), localRoundId, player.PeerId, player.Position.X, player.Position.Y);
            }

        }
        else
        {
            if (players.ContainsKey(localPeerId))
                RpcId(1, nameof(SubmitMovementInput), localRoundId, direction.X, direction.Y);

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
            PlaytestLaunchOptions.SmokeConstruction || PlaytestLaunchOptions.SmokeEngineeringAccess ||
            PlaytestLaunchOptions.SmokePowerLoop || PlaytestLaunchOptions.SmokeHealth ||
            PlaytestLaunchOptions.SmokeGhost || PlaytestLaunchOptions.SmokeExamine ||
            PlaytestLaunchOptions.SmokePull)
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
            replicatedEntities.Remove(peerId);
            stationMapView?.RefreshEntities();
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
        localRoundId = roundLifecycle.RoundId;
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
            : PlaytestLaunchOptions.SmokePowerLoop
            ? new Vector2(8f, -6f)
            : new Vector2(spawn.X, spawn.Y);
        AddPlayer(localPeerId, assignedJob, hostSpawn);
        shiftStartedAtMsec = Time.GetTicksMsec();
        currentRoundEndReport = null;
        roundEndReportDialog.Hide();
        startShiftButton.Disabled = true;
        startShiftButton.Text = "Shift started";
        endShiftButton.Visible = true;
        SetStatus($"Shift started · joining crew can now spawn · {hostAddress}:{PlaytestLaunchOptions.Port}");
        GD.Print($"PLAYTEST_ROUND_STARTED host_job={assignedJob.Id}");
        Rpc(nameof(ReceiveRoundStarted), localRoundId);
        RefreshStatus();
    }

    private void EndShift()
    {
        if (!GetMultiplayer().IsServer()) return;
        RoundEndReport report = CaptureRoundEndReport();
        if (roundLifecycle.TryEnd(localPeerId, localRoundId) != RoundEndResult.Ended) return;
        currentRoundEndReport = report;
        localRoundStarted = false;
        endShiftButton.Visible = false;
        restartShiftButton.Visible = true;
        ShowRoundEndReport(report.RoundId, report.ToDisplayText());
        Rpc(nameof(ReceiveRoundEnded), localRoundId, report.ToDisplayText());
        GD.Print($"PLAYTEST_ROUND_ENDED round={localRoundId}");
    }

    private RoundEndReport CaptureRoundEndReport()
    {
        double durationSeconds = shiftStartedAtMsec == 0 ? 0 : (Time.GetTicksMsec() - shiftStartedAtMsec) / 1000.0;
        return RoundEndReport.Create(localRoundId, durationSeconds,
            players.Values.Select(player => player.Job.Name), sharedInteractionWorld.CaptureAll());
    }

    private void ShowRoundEndReport(int roundId, string reportText)
    {
        if (string.IsNullOrWhiteSpace(reportText)) return;
        SetStatus($"Shift {roundId} ended · review the report, then restart when ready.");
        roundEndReportDialog.DialogText = reportText;
        roundEndReportDialog.PopupCentered(new Vector2I(540, 390));
        bool containsTrackedFacts = reportText.Contains("Crew roles present:", StringComparison.Ordinal) &&
            reportText.Contains("APC lamp:", StringComparison.Ordinal) &&
            reportText.Contains("Not tracked in this prototype:", StringComparison.Ordinal);
        string audience = GetMultiplayer().IsServer() ? "HOST" : "CLIENT";
        GD.Print($"PLAYTEST_ROUND_REPORT_{audience} round={roundId} received=true tracked_state={containsTrackedFacts.ToString().ToLowerInvariant()}");
    }

    private void RestartShift()
    {
        if (!GetMultiplayer().IsServer() ||
            roundLifecycle.TryRestart(localPeerId) != RoundRestartResult.Restarted) return;
        localRoundId = roundLifecycle.RoundId;
        ResetSharedWorld();
        currentRoundEndReport = null;
        startShiftButton.Text = "Start shift";
        startShiftButton.Disabled = false;
        restartShiftButton.Visible = false;
        Rpc(nameof(ReceiveRoundRestarted), localRoundId);
        GD.Print($"PLAYTEST_ROUND_RESTARTED round={localRoundId} phase=PreRound");
        RefreshStatus();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestRoundPhase()
    {
        if (!GetMultiplayer().IsServer()) return;
        int peerId = (int)GetMultiplayer().GetRemoteSenderId();
        RpcId(peerId, nameof(ReceiveRoundPhase), (int)roundLifecycle.Phase, roundLifecycle.RoundId,
            currentRoundEndReport?.ToDisplayText() ?? string.Empty);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundPhase(int phase, int roundId, string reportText)
    {
        if (roundId < localRoundId) return;
        localRoundId = roundId;
        if (phase == (int)SharedRoundPhase.Started)
        {
            localRoundStarted = true;
            RequestCrewSpawnFromClient();
        }
        else if (phase == (int)SharedRoundPhase.Ended)
        {
            localRoundStarted = false;
            ShowRoundEndReport(roundId, reportText);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundStarted(int roundId)
    {
        if (roundId < localRoundId) return;
        localRoundId = roundId;
        localRoundStarted = true;
        roundEndReportDialog.Hide();
        RequestCrewSpawnFromClient();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundEnded(int roundId, string reportText)
    {
        if (roundId != localRoundId) return;
        localRoundStarted = false;
        ShowRoundEndReport(roundId, reportText);
        GD.Print($"PLAYTEST_ROUND_ENDED_CLIENT round={roundId}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoundRestarted(int roundId)
    {
        if (roundId <= localRoundId) return;
        localRoundId = roundId;
        ResetSharedWorld();
        SetStatus($"Shift {roundId} ready · waiting for the host to start.");
        GD.Print($"PLAYTEST_ROUND_RESTARTED_CLIENT round={roundId} crew={players.Count} metal={Entity(StationEntityCatalog.MetalSheetsId).Count(StationEntityField.Amount)} plated={Entity(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated)}");
    }

    private void RequestCrewSpawnFromClient()
    {
        if (GetMultiplayer().IsServer()) return;
        SetStatus("Shift started · requesting your crew role…");
        RpcId(1, nameof(RequestCrewSpawn), localRoundId, PlaytestLaunchOptions.CrewJobId);
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
        if (PlaytestLaunchOptions.SmokeGhost) GetTree().Quit();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestCrewSpawn(int roundId, int jobId)
    {
        if (!GetMultiplayer().IsServer()) return;
        int peerId = (int)GetMultiplayer().GetRemoteSenderId();
        if (peerId <= 1) return;
        if (!roundLifecycle.CanRequestJob || roundId != roundLifecycle.RoundId)
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
        System.Numerics.Vector2 spawnPoint = default;
        if (players.Count >= MaximumPlayers || !StationPlaytestLayout.TryFindFreeSpawn(
                players.Values.Select(player => new System.Numerics.Vector2(player.Position.X, player.Position.Y)),
                out spawnPoint))
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

        Vector2 spawn = PlaytestLaunchOptions.SmokeEngineeringAccess
            ? new Vector2(8f, -5f)
            : PlaytestLaunchOptions.SmokePowerLoop
            ? new Vector2(8f, -7f)
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
        RpcId(peerId, nameof(ReceiveEntityStates), localRoundId,
            StationEntityStateCodec.Encode(sharedInteractionWorld.CaptureAll()));
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
            RpcId(1, nameof(RequestLocalSpeech), localRoundId, text);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestLocalSpeech(int roundId, string message)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        RouteLocalSpeech((int)GetMultiplayer().GetRemoteSenderId(), message);
    }

    private void RouteLocalSpeech(int speakerPeerId, string message)
    {
        if (!roundLifecycle.CanRequestJob || message.Length == 0 || message.Length > 256 ||
            message.IndexOfAny(['\r', '\n', '\0']) >= 0 || !players.TryGetValue(speakerPeerId, out PlayerRuntime? speaker))
            return;

        CrewStat speakerStat = sharedInteractionWorld.GetCrewStat(speakerPeerId) ?? CrewStat.Stable;
        List<int> recipients;
        string channel;
        if (speakerStat == CrewStat.Dead)
        {
            // say_dead: the dead talk only among themselves.
            channel = "DEAD";
            recipients = players.Keys.Where(peer => sharedInteractionWorld.GetCrewStat(peer) == CrewStat.Dead).ToList();
        }
        else if (!speakerStat.CanSpeakLocally())
        {
            if (speakerPeerId == localPeerId) SetStatus("You are unconscious and cannot speak.");
            return;
        }
        else
        {
            // TRAIT_FORCE_WHISPER sets message_range to 1 tile.
            bool whisper = speakerStat.IsForcedToWhisper();
            channel = whisper ? "WHISPER" : "LOCAL";
            float range = whisper ? 1f : LocalSpeechRouter.DefaultHearingRange;
            var listeners = new List<LocalChatListener>(players.Count);
            foreach (PlayerRuntime player in players.Values)
            {
                CrewStat listenerStat = sharedInteractionWorld.GetCrewStat(player.PeerId) ?? CrewStat.Stable;
                // The dead hear speech and whispers within normal range of where they watch from, through walls.
                bool dead = listenerStat == CrewStat.Dead;
                if (player.PeerId != speakerPeerId && !dead && !listenerStat.CanHear()) continue;
                listeners.Add(new LocalChatListener(player.PeerId, PerceptionPosition(player),
                    dead ? LocalSpeechRouter.DefaultHearingRange : range, IgnoresWalls: dead));
            }

            System.Numerics.Vector2 origin = new(speaker.Position.X, speaker.Position.Y);
            recipients = LocalSpeechRouter.FindListeners(speakerPeerId, origin, listeners, HasLocalLineOfSight).ToList();
        }

        foreach (int recipient in recipients)
        {
            if (recipient == 1)
                AppendSpeech(channel, speakerPeerId, speaker.Job.Name, message);
            else
                RpcId(recipient, nameof(ReceiveLocalSpeech), channel, speakerPeerId, speaker.Job.Name, message);
        }

        GD.Print($"PLAYTEST_LOCAL_CHAT_RELAYED sender={speakerPeerId} recipients={recipients.Count} channel={channel}");
    }

    /// <summary>Sends an action or combat line to every conscious crew member who can see where it happened.</summary>
    private void RouteVisibleMessage(string channel, System.Numerics.Vector2 origin, string text, params int[] alwaysInform)
    {
        var listeners = new List<LocalChatListener>(players.Count);
        foreach (PlayerRuntime player in players.Values)
        {
            CrewStat stat = sharedInteractionWorld.GetCrewStat(player.PeerId) ?? CrewStat.Stable;
            bool dead = stat == CrewStat.Dead;
            if (!dead && !stat.CanHear() && !alwaysInform.Contains(player.PeerId)) continue;
            listeners.Add(new LocalChatListener(player.PeerId, PerceptionPosition(player),
                LocalSpeechRouter.DefaultHearingRange, IgnoresWalls: dead));
        }
        var recipients = new HashSet<int>(alwaysInform.Where(players.ContainsKey));
        foreach (LocalChatListener listener in listeners)
            if (System.Numerics.Vector2.Distance(listener.Position, origin) <= listener.HearingRange &&
                (listener.IgnoresWalls || HasLocalLineOfSight(origin, listener.Position)))
                recipients.Add(listener.PeerId);
        foreach (int recipient in recipients)
        {
            if (recipient == 1) AppendActionMessage(channel, text);
            else RpcId(recipient, nameof(ReceiveActionMessage), channel, text);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveActionMessage(string channel, string text) => AppendActionMessage(channel, text);

    private void AppendActionMessage(string channel, string text)
    {
        chatLog.AppendText($"[{channel}] {text}\n");
        chatLog.ScrollToLine(Math.Max(0, chatLog.GetLineCount() - 1));
        GD.Print($"PLAYTEST_ACTION_MESSAGE channel={channel}");
    }

    /// <summary>
    /// Lying bodies do not block movement (tgstation adds TRAIT_UNDENSE when lying down), so a casualty
    /// cannot seal a doorway. A crew member who stands back up blocks again once their tile is free.
    /// </summary>
    private void SyncCrewDensity(PlayerRuntime player, CrewStat stat)
    {
        bool dense = !stat.IsFloored();
        if (dense == player.ObstacleActive) return;
        if (!dense)
        {
            collisionMap.RemoveObject(player.Obstacle);
            player.ObstacleActive = false;
            return;
        }
        int x = Mathf.RoundToInt(player.Position.X);
        int y = Mathf.RoundToInt(player.Position.Y);
        if (collisionMap.GetObject(x, y) != null) return;
        player.Obstacle.X = x;
        player.Obstacle.Y = y;
        collisionMap.AddObject(player.Obstacle);
        player.ObstacleActive = true;
    }

    private static readonly System.Numerics.Vector2 GhostMinimum = new(StationPlaytestLayout.MinX, StationPlaytestLayout.MinY);
    private static readonly System.Numerics.Vector2 GhostMaximum = new(StationPlaytestLayout.MaxX, StationPlaytestLayout.MaxY);

    /// <summary>Where a player hears and sees from: their ghost once they have left the body.</summary>
    private System.Numerics.Vector2 PerceptionPosition(PlayerRuntime player) =>
        sharedInteractionWorld.GetPerceptionPosition(player.PeerId)
        ?? new System.Numerics.Vector2(player.Position.X, player.Position.Y);

    /// <summary>Moves a pulled crew member or fixture into the tile its puller just left.</summary>
    private void MovePulledEntity(int entityId, System.Numerics.Vector2 destination)
    {
        int x = Mathf.RoundToInt(destination.X);
        int y = Mathf.RoundToInt(destination.Y);
        if (players.TryGetValue(entityId, out PlayerRuntime? pulled))
        {
            pulled.Position = new Vector2(x, y);
            pulled.Avatar.Position = pulled.Position;
            if (pulled.ObstacleActive) collisionMap.MoveObject(pulled.Obstacle, x, y);
            sharedInteractionWorld.SetActorPosition(entityId, destination);
        }
        else if (entityObstacles.TryGetValue(entityId, out DynamicObject? obstacle))
        {
            collisionMap.MoveObject(obstacle, x, y);
            sharedInteractionWorld.SetEntityPosition(entityId, destination);
        }
    }

    private void OnCrewStruckUnarmed(int attackerId, int victimId, UnarmedAttackResult result)
    {
        GD.Print($"PLAYTEST_CREW_STRUCK_UNARMED attacker={attackerId} victim={victimId} verb={result.Verb} hit={result.Hit} brute={result.Damage:0} health={sharedInteractionWorld.GetCrewHealth(victimId):0}");
        string target = attackerId == victimId ? "themselves" : CrewName(victimId);
        string text = result.Hit
            ? $"{CrewName(attackerId)} {result.VerbContinuous} {target}!"
            : $"{CrewName(attackerId)}'s {result.Verb} misses {target}!";
        RouteVisibleMessage("COMBAT", CrewPosition(victimId), text, attackerId, victimId);
    }

    private void OnCprStarted(int rescuerId, int patientId)
    {
        GD.Print($"PLAYTEST_CPR_STARTED rescuer={rescuerId} patient={patientId}");
        RouteVisibleMessage("ACTION", CrewPosition(patientId),
            $"{CrewName(rescuerId)} is trying to perform CPR on {CrewName(patientId)}!", rescuerId, patientId);
    }

    private void OnCprPerformed(int rescuerId, int patientId, double healed)
    {
        GD.Print($"PLAYTEST_CPR_PERFORMED rescuer={rescuerId} patient={patientId} oxy_healed={healed:0}");
        RouteVisibleMessage("ACTION", CrewPosition(patientId),
            $"{CrewName(rescuerId)} performs CPR on {CrewName(patientId)}!", rescuerId, patientId);
    }

    private void OnPullStarted(int pullerId, int pulledId)
    {
        GD.Print($"PLAYTEST_PULL_STARTED puller={pullerId} target={pulledId}");
        if (players.ContainsKey(pulledId))
            RouteVisibleMessage("ACTION", CrewPosition(pulledId),
                $"{CrewName(pullerId)} grabs {CrewName(pulledId)} passively.", pullerId, pulledId);
    }

    private void OnPullStopped(int pullerId, int pulledId, PullStopReason reason) =>
        GD.Print($"PLAYTEST_PULL_STOPPED puller={pullerId} target={pulledId} reason={reason}");

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestPull(int roundId, int targetId)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        ResolvePull((int)GetMultiplayer().GetRemoteSenderId(), targetId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestReleasePull(int roundId)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        int senderId = (int)GetMultiplayer().GetRemoteSenderId();
        if (roundLifecycle.CanRequestJob && players.ContainsKey(senderId))
            sharedInteractionWorld.TryReleasePull(senderId);
    }

    /// <summary>Ctrl-click: drop the current pull, or start pulling an adjacent crew member or loose cart.</summary>
    private void ResolvePull(int senderId, int targetId)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId)) return;
        if (sharedInteractionWorld.TryTogglePull(senderId, targetId)) return;
        if (senderId == localPeerId) ReceivePullRejected();
        else RpcId(senderId, nameof(ReceivePullRejected));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePullRejected() =>
        SetStatus("You cannot pull that. Stand next to a crew member or an unanchored cart, with working hands.");

    /// <summary>Shows what the local player pulls and who pulls them, from replicated crew state.</summary>
    private void UpdatePullLabel()
    {
        if (!IsInstanceValid(pullLabel)) return;
        string text = string.Empty;
        foreach (StationEntityState crew in replicatedEntities.All)
        {
            if (crew.Kind != StationEntityKind.Crew) continue;
            int pulled = crew.Count(StationEntityField.Pulling);
            if (crew.Id == localPeerId && pulled != 0)
                text += $"Pulling: {(replicatedEntities.TryGet(pulled, out StationEntityState? target) && target.Kind != StationEntityKind.Crew ? target.Kind.Replace('-', ' ') : $"Crew {pulled}")} · H or ctrl-click to let go\n";
            else if (crew.Id != localPeerId && pulled == localPeerId)
                text += $"Crew {crew.Id} is pulling you · move away to break free\n";
        }
        pullLabel.Text = text.TrimEnd();
        pullLabel.Visible = text.Length > 0;
    }

    /// <summary>
    /// Two-client pull smoke: the remote Assistant ctrl-pulls the Engineer host and walks away, and the host
    /// follows one tile behind. The host then walks off until the pull breaks by distance.
    /// </summary>
    private void DriveSmokePull()
    {
        if (!PlaytestLaunchOptions.SmokePull || !localRoundStarted || players.Count < 2) return;
        int remote = players.Keys.First(peer => peer != localPeerId);
        if (GetMultiplayer().IsServer())
        {
            PlayerRuntime host = players[localPeerId];
            PlayerRuntime puller = players[remote];
            if (smokePullStage == 0)
            {
                smokePullStage = 1;
                StageSmokePlayer(localPeerId, SmokeHealthVictimHall);
                StageSmokePlayer(remote, SmokeHealthAttackerHall);
            }
            else if (smokePullStage == 1 && sharedInteractionWorld.GetPuller(localPeerId) == remote &&
                     puller.Position.Y >= 3 && puller.DeltaX == 0 && puller.DeltaY == 0)
            {
                smokePullStage = 2;
                bool behind = Mathf.RoundToInt(host.Position.X) == Mathf.RoundToInt(puller.Position.X) &&
                              Mathf.RoundToInt(host.Position.Y) == Mathf.RoundToInt(puller.Position.Y) - 1;
                GD.Print($"PLAYTEST_PULL_HOST_FOLLOWED behind={behind} host_x={host.Position.X:0} host_y={host.Position.Y:0}");
                smokePullInput = Vector2I.Left;
            }
            else if (smokePullStage == 2 && sharedInteractionWorld.GetPuller(localPeerId) == null)
            {
                smokePullStage = 3;
                smokePullInput = Vector2I.Zero;
                GD.Print("PLAYTEST_PULL_HOST_OK followed=true broke_free=true");
            }
            return;
        }

        if (!replicatedEntities.TryGet(localPeerId, out StationEntityState? self)) return;
        Vector2 selfPosition = new(self.Position.X, self.Position.Y);
        int pulling = self.Count(StationEntityField.Pulling);
        switch (smokePullStage)
        {
            case 0 when selfPosition.DistanceTo(SmokeHealthAttackerHall) < 0.1f:
                smokePullStage = 1;
                RpcId(1, nameof(RequestPull), localRoundId, remote);
                break;
            case 1 when pulling == remote:
                smokePullStage = 2;
                smokePullInput = Vector2I.Down;
                GD.Print($"PLAYTEST_PULL_CLIENT_STARTED target={remote}");
                break;
            case 2 when selfPosition.Y >= 3:
                smokePullStage = 3;
                smokePullInput = Vector2I.Zero;
                break;
            case 3 when pulling == 0:
                smokePullStage = 4;
                GD.Print("PLAYTEST_PULL_CLIENT_OK started=true released_by_distance=true");
                break;
        }
    }

    private void OnCrewGhosted(int peerId) => GD.Print($"PLAYTEST_CREW_GHOSTED peer={peerId}");

    private string CrewName(int peerId) =>
        players.TryGetValue(peerId, out PlayerRuntime? player) ? $"Crew {peerId} · {player.Job.Name}" : $"Crew {peerId}";

    private System.Numerics.Vector2 CrewPosition(int peerId) =>
        players.TryGetValue(peerId, out PlayerRuntime? player)
            ? new System.Numerics.Vector2(player.Position.X, player.Position.Y)
            : System.Numerics.Vector2.Zero;

    private void OnCrewAttacked(int attackerId, int victimId, double damage)
    {
        GD.Print($"PLAYTEST_CREW_ATTACKED attacker={attackerId} victim={victimId} brute={damage:0} health={sharedInteractionWorld.GetCrewHealth(victimId):0}");
        string target = attackerId == victimId ? "themselves" : CrewName(victimId);
        RouteVisibleMessage("COMBAT", CrewPosition(victimId), $"{CrewName(attackerId)} hits {target} with the wrench!",
            attackerId, victimId);
    }

    private void OnCrewTreated(int healerId, int patientId, double healed)
    {
        GD.Print($"PLAYTEST_CREW_TREATED healer={healerId} patient={patientId} healed={healed:0} health={sharedInteractionWorld.GetCrewHealth(patientId):0}");
        string target = healerId == patientId ? "their own bruises" : $"{CrewName(patientId)}'s bruises";
        RouteVisibleMessage("ACTION", CrewPosition(patientId), $"{CrewName(healerId)} applies a bruise pack to {target}.",
            healerId, patientId);
    }

    private void OnCrewStatChanged(int peerId, CrewStat before, CrewStat after)
    {
        GD.Print($"PLAYTEST_CREW_STAT peer={peerId} from={before} to={after}");
        string text = after switch
        {
            CrewStat.SoftCrit => $"{CrewName(peerId)} collapses to the floor!",
            CrewStat.HardCrit => $"{CrewName(peerId)} falls unconscious.",
            CrewStat.Dead => $"{CrewName(peerId)} seizes up and falls limp, their eyes dead and lifeless...",
            _ => $"{CrewName(peerId)} comes to and gets back up.",
        };
        RouteVisibleMessage("ACTION", CrewPosition(peerId), text, peerId);
    }

    private bool HasLocalLineOfSight(System.Numerics.Vector2 from, System.Numerics.Vector2 to)
    {
        var movementState = new PlayerMovementState();
        return GridLineOfSight.IsClear(from, to, (x, y) => !collisionMap.IsPassable(x, y, movementState));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLocalSpeech(string channel, int speakerPeerId, string jobName, string message)
    {
        AppendSpeech(channel, speakerPeerId, jobName, message);
    }

    private void AppendSpeech(string channel, int speakerPeerId, string jobName, string message)
    {
        chatLog.AppendText($"[{channel}] Crew {speakerPeerId} · {jobName}: {message}\n");
        chatLog.ScrollToLine(Math.Max(0, chatLog.GetLineCount() - 1));
        GD.Print($"PLAYTEST_LOCAL_CHAT_RECEIVED speaker={speakerPeerId}");
        if (PlaytestLaunchOptions.SmokeGhost && speakerPeerId != localPeerId && channel == "LOCAL")
            smokeGhostHeardLiving = true;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SubmitMovementInput(int roundId, int deltaX, int deltaY)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
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
        replicatedEntities.Remove(peerId);
        stationMapView?.RefreshEntities();
        collisionMap.RemoveObject(player.Obstacle);
        player.Avatar.QueueFree();
        RefreshStatus();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceivePosition(int roundId, int peerId, float x, float y)
    {
        if (roundId != localRoundId || !localRoundStarted) return;
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
        if (!roundLifecycle.CanRequestJob) return;
        if (!MovementCommandAuthorization.IsValid(ownerPeerId, senderPeerId, deltaX, deltaY))
            return;
        if (!players.TryGetValue(ownerPeerId, out PlayerRuntime? player)) return;
        player.DeltaX = deltaX;
        player.DeltaY = deltaY;
    }

    private Vector2I ReadLocalInput()
    {
        if (smokeGhostMoving) return new Vector2I(1, 1);
        if (smokePullInput != Vector2I.Zero) return smokePullInput;
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
        stationMapView.SetEntities(replicatedEntities);
        AddChild(stationMapView);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false } intentKey &&
            intentKey.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4)
        {
            SetSelectedIntent(intentKey.Keycode switch
            {
                Key.Key1 => Intent.Help,
                Key.Key2 => Intent.Disarm,
                Key.Key3 => Intent.Grab,
                _ => Intent.Harm,
            });
            return;
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab } &&
            localRoundStarted && players.ContainsKey(GetMultiplayer().GetUniqueId()))
        {
            if (GetMultiplayer().IsServer())
                ResolveActiveHandToggle(localPeerId);
            else
                RpcId(1, nameof(RequestActiveHandToggle), localRoundId);
            return;
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.H } &&
            localRoundStarted && players.ContainsKey(GetMultiplayer().GetUniqueId()))
        {
            if (GetMultiplayer().IsServer())
                sharedInteractionWorld.TryReleasePull(localPeerId);
            else
                RpcId(1, nameof(RequestReleasePull), localRoundId);
            return;
        }

        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q } &&
            localRoundStarted && players.ContainsKey(GetMultiplayer().GetUniqueId()))
        {
            if (GetMultiplayer().IsServer())
                ResolveSharedDrop(localPeerId);
            else
                RpcId(1, nameof(RequestSharedDrop), localRoundId);
            return;
        }

        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click ||
            !localRoundStarted || !players.ContainsKey(GetMultiplayer().GetUniqueId())) return;

        Vector2 clickPosition = GetGlobalMousePosition();
        if (click.ShiftPressed)
        {
            int? examined = replicatedEntities.FindNearestExaminable(
                new System.Numerics.Vector2(clickPosition.X, clickPosition.Y), 0.7f);
            if (examined is null) return;
            if (GetMultiplayer().IsServer()) ResolveExamine(localPeerId, examined.Value);
            else RpcId(1, nameof(RequestExamine), localRoundId, examined.Value);
            return;
        }

        int? targetId = replicatedEntities.FindNearestTarget(
            new System.Numerics.Vector2(clickPosition.X, clickPosition.Y), 0.7f);
        if (targetId is null) return;

        if (click.CtrlPressed)
        {
            if (GetMultiplayer().IsServer()) ResolvePull(localPeerId, targetId.Value);
            else RpcId(1, nameof(RequestPull), localRoundId, targetId.Value);
            return;
        }

        bool altClick = click.AltPressed;
        if (GetMultiplayer().IsServer())
            ResolveSharedInteraction(localPeerId, targetId.Value, (int)selectedIntent, altClick);
        else
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, targetId.Value, (int)selectedIntent, altClick);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestExamine(int roundId, int targetId)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        ResolveExamine((int)GetMultiplayer().GetRemoteSenderId(), targetId);
    }

    /// <summary>Shift-click: the server describes the target to the examiner alone.</summary>
    private void ResolveExamine(int senderId, int targetId)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId)) return;
        ExamineResult result = sharedInteractionWorld.TryExamine(senderId, targetId, out IReadOnlyList<string> lines);
        string text = result switch
        {
            ExamineResult.Ok => string.Join("\n", lines),
            ExamineResult.Unconscious => "You are unconscious and cannot see.",
            ExamineResult.OutOfView => "You cannot see that from here.",
            _ => string.Empty,
        };
        GD.Print($"PLAYTEST_EXAMINE peer={senderId} target={targetId} result={result}");
        if (text.Length == 0) return;
        if (senderId == localPeerId) ReceiveExamine(result == ExamineResult.Ok, text);
        else RpcId(senderId, nameof(ReceiveExamine), result == ExamineResult.Ok, text);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveExamine(bool seen, string text)
    {
        if (seen)
        {
            chatLog.AppendText($"[EXAMINE] {text}\n");
            chatLog.ScrollToLine(Math.Max(0, chatLog.GetLineCount() - 1));
            smokeExamineReceived++;
        }
        else
        {
            SetStatus(text);
            smokeExamineRejected = true;
        }
        string first = text.Split('\n')[0];
        GD.Print($"PLAYTEST_EXAMINE_RECEIVED seen={seen} first={first}");
    }

    /// <summary>
    /// Two-client examine smoke: the remote Assistant looks at the wrench and at the Engineer host from
    /// the hall, then is refused a look at the bruise packs behind Medical's walls.
    /// </summary>
    private void DriveSmokeExamine()
    {
        if (!PlaytestLaunchOptions.SmokeExamine || !localRoundStarted || players.Count < 2) return;
        int remote = players.Keys.First(peer => peer != localPeerId);
        if (GetMultiplayer().IsServer())
        {
            if (smokeExamineStage != 0) return;
            smokeExamineStage = 1;
            StageSmokePlayer(localPeerId, SmokeHealthVictimHall);
            StageSmokePlayer(remote, SmokeHealthAttackerHall);
            return;
        }

        if (!replicatedEntities.TryGet(localPeerId, out StationEntityState? self)) return;
        Vector2 selfPosition = new(self.Position.X, self.Position.Y);
        switch (smokeExamineStage)
        {
            case 0 when selfPosition.DistanceTo(SmokeHealthAttackerHall) < 0.1f:
                smokeExamineStage = 1;
                RpcId(1, nameof(RequestExamine), localRoundId, StationEntityCatalog.WrenchId);
                break;
            case 1 when smokeExamineReceived == 1:
                smokeExamineStage = 2;
                RpcId(1, nameof(RequestExamine), localRoundId, remote);
                break;
            case 2 when smokeExamineReceived == 2:
                smokeExamineStage = 3;
                RpcId(1, nameof(RequestExamine), localRoundId, StationEntityCatalog.BruisePackId);
                break;
            case 3 when smokeExamineRejected:
                smokeExamineStage = 4;
                GD.Print("PLAYTEST_EXAMINE_CLIENT_OK object=true crew=true walled_off=true");
                break;
        }
    }

    private void SetSelectedIntent(Intent intent)
    {
        selectedIntent = intent;
        if (IsInstanceValid(intentLabel))
            intentLabel.Text = $"Intent: {intent.ToString().ToUpperInvariant()} · 1 help · 2 disarm · 3 grab · 4 harm";
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSharedInteraction(int roundId, int targetId, int intentValue, bool altClick)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        int senderId = (int)GetMultiplayer().GetRemoteSenderId();
        ResolveSharedInteraction(senderId, targetId, intentValue, altClick);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSharedDrop(int roundId)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
        ResolveSharedDrop((int)GetMultiplayer().GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestActiveHandToggle(int roundId)
    {
        if (!GetMultiplayer().IsServer() || roundId != roundLifecycle.RoundId) return;
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
        activeHandItemName = itemName;
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
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, StationEntityCatalog.WrenchId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_INTERACTION_REQUEST pickup");
            return;
        }

        StationEntityState wrench = Entity(StationEntityCatalog.WrenchId);
        bool cartAnchored = Entity(StationEntityCatalog.ServiceCartId).Flag(StationEntityField.Anchored);
        if (wrench.Holder == localPeerId && !smokeInteractionUseRequested)
        {
            smokeInteractionUseRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, StationEntityCatalog.ServiceCartId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_INTERACTION_REQUEST wrench-use");
            return;
        }

        if (wrench.Holder == localPeerId && !cartAnchored && !smokeInteractionDropRequested)
        {
            smokeInteractionDropRequested = true;
            RpcId(1, nameof(RequestSharedDrop), localRoundId);
            GD.Print("PLAYTEST_INTERACTION_REQUEST drop");
            return;
        }

        if (smokeInteractionDropRequested && wrench.Holder == null && !cartAnchored)
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
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, StationEntityCatalog.MetalSheetsId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_CONSTRUCTION_REQUEST pickup-metal");
            return;
        }

        StationEntityState metal = Entity(StationEntityCatalog.MetalSheetsId);
        if (metal.Holder == localPeerId && !smokeConstructionStarted)
        {
            smokeConstructionStarted = true;
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, StationEntityCatalog.GirderId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_CONSTRUCTION_REQUEST plate-girder");
            return;
        }

        if (Entity(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated) &&
            metal.Count(StationEntityField.Amount) == 0)
        {
            smokeConstructionCompleted = true;
            GD.Print("PLAYTEST_GIRDER_PLATING_OK engineer=true sheets_consumed=2 duration_seconds=4 replicated=true");
        }
    }

    private void DriveSmokeRoundRestart()
    {
        if (!PlaytestLaunchOptions.SmokeRoundRestart || !GetMultiplayer().IsServer()) return;
        if (smokeRoundRestartStage == 0 && players.Count >= 2 &&
            roundLifecycle.Phase == SharedRoundPhase.Started &&
            ServerEntity(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated))
        {
            smokeRoundRestartStage = 1;
            EndShift();
            smokeRoundRestartAfterMsec = Time.GetTicksMsec() + 250;
        }
        else if (smokeRoundRestartStage == 1 && roundLifecycle.Phase == SharedRoundPhase.Ended &&
                 Time.GetTicksMsec() >= smokeRoundRestartAfterMsec)
        {
            smokeRoundRestartStage = 2;
            RestartShift();
            smokeRoundRestartAfterMsec = Time.GetTicksMsec() + 250;
        }
        else if (smokeRoundRestartStage == 2 && roundLifecycle.Phase == SharedRoundPhase.PreRound &&
                 Time.GetTicksMsec() >= smokeRoundRestartAfterMsec)
        {
            smokeRoundRestartStage = 3;
            StartShift();
        }
        else if (smokeRoundRestartStage == 3 && roundLifecycle.Phase == SharedRoundPhase.Started &&
                 players.Count >= 2 &&
                 !ServerEntity(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated) &&
                 ServerEntity(StationEntityCatalog.MetalSheetsId).Count(StationEntityField.Amount) == 2 &&
                 !ServerEntity(StationEntityCatalog.EngineeringAirlockId).Flag(StationEntityField.Open) &&
                 ServerEntity(StationEntityCatalog.WrenchId).Holder is null)
        {
            smokeRoundRestartStage = 4;
            GD.Print($"PLAYTEST_ROUND_RESTART_OK round={localRoundId} crew={players.Count} metal=2 plated=false airlock=false wrench_owner=0");
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
                ResolveSharedInteraction(localPeerId, StationEntityCatalog.EngineeringAirlockId,
                    (int)Intent.Help, false);
            }
            return;
        }

        if (!smokeEngineeringAccessRequested)
        {
            smokeEngineeringAccessRequested = true;
            RpcId(1, nameof(RequestSharedInteraction), localRoundId, StationEntityCatalog.EngineeringAirlockId,
                (int)Intent.Help, false);
            GD.Print("PLAYTEST_ENGINEERING_ACCESS_REQUEST assistant_try");
        }
    }

    private void DriveSmokePowerLoop()
    {
        if (!PlaytestLaunchOptions.SmokePowerLoop || GetMultiplayer().IsServer() ||
            !localRoundStarted || players.Count < 2 || smokePowerCompleted) return;

        StationEntityState fuel = Entity(StationEntityCatalog.GeneratorFuelId);
        StationEntityState generator = Entity(StationEntityCatalog.FuelGeneratorId);
        StationEntityState storage = Entity(StationEntityCatalog.PowerStorageId);
        int? fuelOwner = fuel.Holder;
        int fuelCount = fuel.Count(StationEntityField.Amount);
        double fuelSeconds = generator.Get(StationEntityField.FuelSeconds);
        bool generatorRunning = generator.Flag(StationEntityField.Running);
        bool lampOn = Entity(StationEntityCatalog.ApcLampId).Flag(StationEntityField.Powered);
        switch (smokePowerStage)
        {
            case 0:
                smokePowerStage = 1;
                RequestSmokeInteraction(StationEntityCatalog.GeneratorFuelId);
                break;
            case 1 when fuelOwner == localPeerId &&
                        fuelCount == 3:
                smokePowerStage = 2;
                RequestSmokeInteraction(StationEntityCatalog.FuelGeneratorId);
                break;
            case 2 when fuelOwner == localPeerId &&
                        fuelCount == 2 && fuelSeconds >= 4:
                smokePowerStage = 3;
                RequestSmokeInteraction(StationEntityCatalog.FuelGeneratorId);
                break;
            case 3 when fuelOwner == localPeerId &&
                        fuelCount == 1 && fuelSeconds >= 8:
                smokePowerStage = 4;
                RpcId(1, nameof(RequestActiveHandToggle), localRoundId);
                break;
            case 4 when activeHandItemName.Length == 0:
                smokePowerStage = 5;
                RequestSmokeInteraction(StationEntityCatalog.FuelGeneratorId);
                break;
            case 5 when generatorRunning:
                smokePowerStage = 6;
                RequestSmokeInteraction(StationEntityCatalog.PowerStorageId);
                break;
            case 6 when storage.Flag(StationEntityField.OutputEnabled) && lampOn:
                if (!smokePowerSawOn)
                {
                    smokePowerSawOn = true;
                    GD.Print("PLAYTEST_POWER_CLIENT_ON replicated=true");
                }
                break;
            case 6 when smokePowerSawOn && !lampOn:
                smokePowerSawOutage = true;
                smokePowerStage = 7;
                RpcId(1, nameof(RequestActiveHandToggle), localRoundId);
                GD.Print("PLAYTEST_POWER_CLIENT_OUTAGE replicated=true");
                break;
            case 7 when activeHandItemName == "generator fuel":
                smokePowerStage = 8;
                RequestSmokeInteraction(StationEntityCatalog.FuelGeneratorId);
                break;
            case 8 when fuelCount == 0 &&
                        fuelSeconds >= 4 && !generatorRunning:
                smokePowerStage = 9;
                RequestSmokeInteraction(StationEntityCatalog.FuelGeneratorId);
                break;
            case 9 when generatorRunning:
                smokePowerStage = 10;
                break;
            case 10 when lampOn:
                smokePowerSawRecovery = true;
                smokePowerCompleted = true;
                GD.Print($"PLAYTEST_POWER_LOOP_CLIENT_OK engineer={localPeerId} job={players[localPeerId].Job.Id} role={players[localPeerId].Job.Name} on={smokePowerSawOn} outage={smokePowerSawOutage} recovery={smokePowerSawRecovery} replicated=true");
                break;
        }
    }

    private static readonly Vector2 SmokeHealthVictimHall = new(-1, 0);
    private static readonly Vector2 SmokeHealthAttackerHall = new(-1, 1);
    private static readonly Vector2 SmokeHealthVictimMedical = new(-7, -6);
    private static readonly Vector2 SmokeHealthAttackerMedical = new(-8, -6);

    /// <summary>
    /// Two-client survival smoke. The host stages positions (movement has its own smoke); the remote
    /// Assistant then uses ordinary requests: wrench pickup, harm-intent hits until the Engineer host
    /// collapses, a bruise pack from Medical, and treatment until both peers see the host stand again.
    /// </summary>
    private void DriveSmokeHealth()
    {
        if (!PlaytestLaunchOptions.SmokeHealth || !localRoundStarted || players.Count < 2) return;
        if (GetMultiplayer().IsServer())
        {
            DriveSmokeHealthHost();
            return;
        }

        int remote = players.Keys.First(peer => peer != localPeerId);
        if (!replicatedEntities.TryGet(localPeerId, out StationEntityState? self) ||
            !replicatedEntities.TryGet(remote, out StationEntityState? victim)) return;
        var victimStat = (CrewStat)victim.Count(StationEntityField.Stat);
        Vector2 selfPosition = new(self.Position.X, self.Position.Y);
        switch (smokeHealthStage)
        {
            case 0 when selfPosition.DistanceTo(SmokeHealthAttackerHall) < 0.1f:
                smokeHealthStage = 1;
                RequestSmokeInteraction(StationEntityCatalog.WrenchId);
                break;
            case 1 when Entity(StationEntityCatalog.WrenchId).Holder == localPeerId:
                smokeHealthStage = 2;
                SetSelectedIntent(Intent.Harm);
                break;
            case 2 when victimStat == CrewStat.Stable && Time.GetTicksMsec() >= smokeHealthNextAttackMsec:
                smokeHealthNextAttackMsec = Time.GetTicksMsec() + 900;
                RpcId(1, nameof(RequestSharedInteraction), localRoundId, remote, (int)selectedIntent, false);
                break;
            case 2 when victimStat == CrewStat.SoftCrit:
                smokeHealthStage = 3;
                GD.Print($"PLAYTEST_HEALTH_CLIENT_SAW_CRIT victim={remote} health={victim.Get(StationEntityField.Health):0}");
                break;
            case 3 when selfPosition.DistanceTo(SmokeHealthAttackerMedical) < 0.1f:
                smokeHealthStage = 4;
                SetSelectedIntent(Intent.Help);
                RpcId(1, nameof(RequestActiveHandToggle), localRoundId);
                break;
            case 4 when activeHandItemName.Length == 0:
                smokeHealthStage = 5;
                RequestSmokeInteraction(StationEntityCatalog.BruisePackId);
                break;
            case 5 when activeHandItemName == "bruise pack":
                smokeHealthStage = 6;
                RpcId(1, nameof(RequestSharedInteraction), localRoundId, remote, (int)selectedIntent, false);
                break;
            case 6 when victimStat == CrewStat.Stable:
                smokeHealthStage = 7;
                GD.Print($"PLAYTEST_HEALTH_CLIENT_OK crit=true treated=true victim_health={victim.Get(StationEntityField.Health):0} packs={Entity(StationEntityCatalog.BruisePackId).Count(StationEntityField.Amount)} replicated=true");
                break;
        }
    }

    private static readonly Vector2 SmokeGhostFarCorner = new(11, 8);

    /// <summary>
    /// Two-client ghost smoke. The remote Assistant kills the Engineer host with harm-intent wrench hits.
    /// The host then leaves its body, drifts through station walls, hears the living from where the ghost
    /// is, and speaks only to the dead. The client checks that the body stayed and the ghost is hidden.
    /// </summary>
    private void DriveSmokeGhost()
    {
        if (!PlaytestLaunchOptions.SmokeGhost || !localRoundStarted || players.Count < 2) return;
        int remote = players.Keys.First(peer => peer != localPeerId);
        if (GetMultiplayer().IsServer())
        {
            DriveSmokeGhostHost(remote);
            return;
        }

        if (!replicatedEntities.TryGet(localPeerId, out StationEntityState? self) ||
            !replicatedEntities.TryGet(remote, out StationEntityState? victim)) return;
        var victimStat = (CrewStat)victim.Count(StationEntityField.Stat);
        Vector2 selfPosition = new(self.Position.X, self.Position.Y);
        Vector2 bodyPosition = new(victim.Position.X, victim.Position.Y);
        switch (smokeGhostStage)
        {
            case 0 when selfPosition.DistanceTo(SmokeHealthAttackerHall) < 0.1f:
                smokeGhostStage = 1;
                RequestSmokeInteraction(StationEntityCatalog.WrenchId);
                break;
            case 1 when Entity(StationEntityCatalog.WrenchId).Holder == localPeerId:
                smokeGhostStage = 2;
                SetSelectedIntent(Intent.Harm);
                break;
            case 2 when victimStat != CrewStat.Dead && Time.GetTicksMsec() >= smokeGhostNextAttackMsec:
                smokeGhostNextAttackMsec = Time.GetTicksMsec() + 900;
                RpcId(1, nameof(RequestSharedInteraction), localRoundId, remote, (int)selectedIntent, false);
                break;
            case 2 when victimStat == CrewStat.Dead:
                smokeGhostStage = 3;
                GD.Print($"PLAYTEST_GHOST_CLIENT_SAW_DEATH victim={remote}");
                break;
            case 3 when selfPosition.DistanceTo(SmokeGhostFarCorner) < 0.1f &&
                        victim.Flag(StationEntityField.Ghost) && victim.Get(StationEntityField.GhostY) >= 5.9:
                smokeGhostStage = 4;
                bool ghostHidden = players[remote].Avatar is StationCrewAvatar { GhostVisible: false };
                bool bodyStayed = bodyPosition.DistanceTo(SmokeHealthVictimHall) < 0.1f;
                GD.Print($"PLAYTEST_GHOST_CLIENT_OK body_dead=true body_stayed={bodyStayed} ghost_hidden={ghostHidden}");
                SubmitSpeech("SMOKE_GHOST_LIVING");
                break;
        }
    }

    private void DriveSmokeGhostHost(int remote)
    {
        CrewStat hostStat = sharedInteractionWorld.GetCrewStat(localPeerId) ?? CrewStat.Stable;
        if (smokeGhostStage == 0)
        {
            smokeGhostStage = 1;
            StageSmokePlayer(localPeerId, SmokeHealthVictimHall);
            StageSmokePlayer(remote, SmokeHealthAttackerHall);
            GD.Print("PLAYTEST_GHOST_STAGED area=hall");
        }
        else if (smokeGhostStage == 1 && hostStat == CrewStat.Dead)
        {
            smokeGhostStage = 2;
            ResolveSharedInteraction(localPeerId, StationEntityCatalog.MetalSheetsId, (int)Intent.Help, false);
            StageSmokePlayer(remote, SmokeGhostFarCorner);
            smokeGhostMoving = true;
            GD.Print("PLAYTEST_GHOST_HOST_DEAD leaving_body=true");
        }
        else if (smokeGhostStage == 2 && sharedInteractionWorld.GetGhostPosition(localPeerId) is System.Numerics.Vector2 ghost)
        {
            if (!collisionMap.IsPassable(Mathf.RoundToInt(ghost.X), Mathf.RoundToInt(ghost.Y), new PlayerMovementState()))
                smokeGhostCrossedWall = true;
            if (ghost.Y < 6) return;
            smokeGhostStage = 3;
            smokeGhostMoving = false;
            bool bodyStayed = players[localPeerId].Position.DistanceTo(SmokeHealthVictimHall) < 0.1f;
            GD.Print($"PLAYTEST_GHOST_HOST_THROUGH_WALL wall={smokeGhostCrossedWall} body_stayed={bodyStayed} ghost_x={ghost.X:0.0} ghost_y={ghost.Y:0.0}");
        }
        else if (smokeGhostStage == 3 && smokeGhostHeardLiving)
        {
            smokeGhostStage = 4;
            SubmitSpeech("SMOKE_GHOST_DEAD");
            GD.Print("PLAYTEST_GHOST_HOST_OK heard_living=true");
            GetTree().CreateTimer(2.0).Timeout += () => GetTree().Quit();
        }
    }

    private void DriveSmokeHealthHost()
    {
        int remote = players.Keys.First(peer => peer != localPeerId);
        CrewStat hostStat = sharedInteractionWorld.GetCrewStat(localPeerId) ?? CrewStat.Stable;
        if (smokeHealthStage == 0)
        {
            smokeHealthStage = 1;
            StageSmokePlayer(localPeerId, SmokeHealthVictimHall);
            StageSmokePlayer(remote, SmokeHealthAttackerHall);
            GD.Print("PLAYTEST_HEALTH_STAGED area=hall");
        }
        else if (smokeHealthStage == 1 && hostStat == CrewStat.SoftCrit)
        {
            smokeHealthStage = 2;
            smokeHealthHostSawCrit = true;
            GD.Print($"PLAYTEST_HEALTH_HOST_CRIT health={sharedInteractionWorld.GetCrewHealth(localPeerId):0}");
            ResolveSharedInteraction(localPeerId, StationEntityCatalog.MetalSheetsId, (int)Intent.Help, false);
            smokeHealthHostBlocked = !sharedInteractionWorld.TryToggleActiveHand(localPeerId);
            GD.Print($"PLAYTEST_HEALTH_HOST_BLOCKED hands_blocked={smokeHealthHostBlocked}");
            StageSmokePlayer(localPeerId, SmokeHealthVictimMedical);
            StageSmokePlayer(remote, SmokeHealthAttackerMedical);
            GD.Print("PLAYTEST_HEALTH_STAGED area=medical");
        }
        else if (smokeHealthStage == 2 && hostStat == CrewStat.Stable && !smokeHealthHostSawRecovery)
        {
            smokeHealthStage = 3;
            smokeHealthHostSawRecovery = true;
            bool canAct = sharedInteractionWorld.TryToggleActiveHand(localPeerId);
            GD.Print($"PLAYTEST_HEALTH_HOST_RECOVERED crit={smokeHealthHostSawCrit} blocked={smokeHealthHostBlocked} can_act={canAct} health={sharedInteractionWorld.GetCrewHealth(localPeerId):0}");
        }
    }

    /// <summary>Smoke-only server placement, so the survival smoke does not depend on scripted walking.</summary>
    private void StageSmokePlayer(int peerId, Vector2 position)
    {
        if (!players.TryGetValue(peerId, out PlayerRuntime? player)) return;
        player.DeltaX = 0;
        player.DeltaY = 0;
        player.Position = position;
        player.Avatar.Position = position;
        if (player.ObstacleActive)
            collisionMap.MoveObject(player.Obstacle, Mathf.RoundToInt(position.X), Mathf.RoundToInt(position.Y));
        sharedInteractionWorld.SetActorPosition(peerId, new System.Numerics.Vector2(position.X, position.Y));
        sharedInteractionWorld.PublishChanges();
    }

    private void RequestSmokeInteraction(int targetId) =>
        RpcId(1, nameof(RequestSharedInteraction), localRoundId, targetId, (int)Intent.Help, false);

    private void ResolveSharedInteraction(int senderId, int targetId, int intentValue, bool altClick)
    {
        if (!roundLifecycle.CanRequestJob || !players.ContainsKey(senderId) ||
            intentValue is < (int)Intent.Help or > (int)Intent.Harm) return;
        bool targetsGirder = sharedInteractionWorld.GetState(targetId)?.Kind == StationEntityKind.Girder;
        if (!sharedInteractionWorld.TryInteract(senderId, targetId, (Intent)intentValue, altClick))
        {
            bool accessDenied = sharedInteractionWorld.IsAccessDenied(senderId, targetId);
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
            else if (sharedInteractionWorld.GetCrewStat(senderId) is CrewStat stat && !stat.CanUseHands())
            {
                if (GetMultiplayer().IsServer())
                    GD.Print($"PLAYTEST_INCAPACITATED_ACTION_REJECTED peer={senderId} stat={stat}");
                if (senderId == localPeerId) ReceiveIncapacitatedRejection((int)stat);
                else RpcId(senderId, nameof(ReceiveIncapacitatedRejection), (int)stat);
            }
            else if (senderId == localPeerId) SetStatus(targetsGirder
                    ? "Girder plating requires two metal sheets in your active hand and a clear reach to the girder."
                    : "Too far away, blocked, or you need the right item in your active hand and intent.");
            else RpcId(senderId, nameof(RejectSharedInteraction));
        }
        else
        {
            if (GetMultiplayer().IsServer() && targetsGirder)
                GD.Print($"PLAYTEST_GIRDER_PLATING_STARTED peer={senderId}");
            SendActiveHand(senderId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectSharedInteraction() =>
        SetStatus("Too far away, blocked, or you need the right item in your active hand and intent.");

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveIncapacitatedRejection(int statValue)
    {
        var stat = (CrewStat)statValue;
        SetStatus(stat == CrewStat.Dead ? "You are dead. The living world is out of reach." :
            $"You are {stat.DisplayName().ToLowerInvariant()} and cannot use your hands.");
        GD.Print($"PLAYTEST_INCAPACITATED_REJECTION_RECEIVED stat={stat}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEngineeringAccessDenied()
    {
        smokeEngineeringAccessDenied = true;
        SetStatus("Engineering airlock denied · Engineer ID clearance required.");
        GD.Print("PLAYTEST_ENGINEERING_ACCESS_DENIED_RECEIVED");
    }

    private StationEntityState Entity(int id) => replicatedEntities.Get(id);

    private StationEntityState ServerEntity(int id) => sharedInteractionWorld.GetState(id)!;

    private void OnEntitiesChanged(IReadOnlyList<StationEntityState> changed)
    {
        if (!GetMultiplayer().IsServer()) return;
        ApplyEntityStates(changed);
        if (changed.Any(state => state.IsItem))
            foreach (PlayerRuntime player in players.Values)
                SendActiveHand(player.PeerId);
        if (PlaytestLaunchOptions.SmokeInteractions && changed.Any(state =>
                state.Id is StationEntityCatalog.WrenchId or StationEntityCatalog.ServiceCartId))
            GD.Print($"PLAYTEST_INTERACTION_SERVER_STATE owner={Entity(StationEntityCatalog.WrenchId).Holder ?? 0} anchored={Entity(StationEntityCatalog.ServiceCartId).Flag(StationEntityField.Anchored)}");
        if (GetMultiplayer().MultiplayerPeer != null)
            Rpc(nameof(ReceiveEntityStates), localRoundId, StationEntityStateCodec.Encode(changed));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEntityStates(int roundId, byte[] payload)
    {
        if (roundId != localRoundId || !StationEntityStateCodec.TryDecode(payload, out IReadOnlyList<StationEntityState> states))
            return;
        ApplyEntityStates(states);
        if (PlaytestLaunchOptions.SmokeRoundRestart && !smokeRoundRestartClientVerified && localRoundId == 2 &&
            players.Count >= 2 && Entity(StationEntityCatalog.MetalSheetsId).Count(StationEntityField.Amount) == 2 &&
            !Entity(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated))
        {
            smokeRoundRestartClientVerified = true;
            GD.Print($"PLAYTEST_ROUND_RESTART_CLIENT_OK round={localRoundId} crew={players.Count} metal=2 plated=false");
        }
    }

    /// <summary>Updates the local view, collision and logs from the server's entity states.</summary>
    private void ApplyEntityStates(IReadOnlyList<StationEntityState> states)
    {
        bool isServer = GetMultiplayer().IsServer();
        foreach (StationEntityState state in states)
        {
            StationEntityState? previous = replicatedEntities.TryGet(state.Id, out StationEntityState? known) ? known : null;
            bool wasOpen = previous?.Flag(StationEntityField.Open) ?? false;
            bool wasPlated = previous?.Flag(StationEntityField.Plated) ?? false;
            if (state.Kind == StationEntityKind.Girder && state.Flag(StationEntityField.Plated) && !wasPlated && isServer)
                GD.Print("PLAYTEST_GIRDER_PLATED host_state=true metal_sheets_remaining=0");
            if (state.Kind == StationEntityKind.EngineeringAirlock && state.Flag(StationEntityField.Open) && !wasOpen)
            {
                if (isServer)
                    GD.Print("PLAYTEST_ENGINEERING_AIRLOCK_OPENED peer=1 clearance=Engine");
                else if (PlaytestLaunchOptions.SmokeEngineeringAccess && smokeEngineeringAccessDenied &&
                         !smokeEngineeringAccessVerified)
                {
                    smokeEngineeringAccessVerified = true;
                    GD.Print("PLAYTEST_ENGINEERING_ACCESS_OK denied=true engineer_open=true replicated=true");
                }
            }
        }
        replicatedEntities.Apply(states);
        SyncEntityObstacles(states);
        foreach (StationEntityState state in states)
            if (state.Kind == StationEntityKind.Crew) PresentCrew(state);
        stationMapView?.RefreshEntities();
        if (isServer && PlaytestLaunchOptions.SmokePowerLoop) ObserveSmokePowerOnHost();
    }

    /// <summary>Shows a crew body's replicated condition on its avatar and, for the local player, the HUD.</summary>
    private void PresentCrew(StationEntityState state)
    {
        var stat = (CrewStat)state.Count(StationEntityField.Stat);
        if (players.TryGetValue(state.Id, out PlayerRuntime? player) && player.Avatar is StationCrewAvatar avatar)
            avatar.SetCondition(stat, state.Get(StationEntityField.Health));
        PresentGhost(state);
        UpdatePullLabel();
        if (state.Id != localPeerId) return;
        // Dying changes which ghosts the local player may see.
        foreach (StationEntityState other in replicatedEntities.All)
            if (other.Kind == StationEntityKind.Crew && other.Id != localPeerId) PresentGhost(other);
        if (!IsInstanceValid(healthLabel)) return;
        string treatment = state.Count(StationEntityField.TreatmentPatient) > 0
            ? $" · applying bruise pack {state.Get(StationEntityField.Progress):0.0}s"
            : state.Count(StationEntityField.CprPatient) > 0 ? " · performing CPR, hold still"
            : string.Empty;
        healthLabel.Text = $"Health: {state.Get(StationEntityField.Health):0}/{CrewHealth.MaxHealth:0} · {stat.DisplayName()}" +
                           $" · brute {state.Get(StationEntityField.BruteLoss):0} · oxy {state.Get(StationEntityField.OxyLoss):0}{treatment}";
        if (stat == CrewStat.Dead)
            healthLabel.Text += state.Flag(StationEntityField.Ghost)
                ? "\nYou are a ghost. WASD drifts through walls; the living cannot see or hear you."
                : "\nYou have died. Move (WASD) to leave your body.";
        healthLabel.Modulate = stat switch
        {
            CrewStat.Stable => Colors.White,
            CrewStat.Dead => new Color("#9aa3a8"),
            _ => new Color("#ff8a7a"),
        };
    }

    /// <summary>Draws a dead player's ghost for themselves and for other dead players only.</summary>
    private void PresentGhost(StationEntityState state)
    {
        if (!players.TryGetValue(state.Id, out PlayerRuntime? player) || player.Avatar is not StationCrewAvatar avatar)
            return;
        CrewStat localStat = replicatedEntities.TryGet(localPeerId, out StationEntityState? self)
            ? (CrewStat)self.Count(StationEntityField.Stat)
            : CrewStat.Stable;
        Vector2? ghost = state.Flag(StationEntityField.Ghost)
            ? new Vector2((float)state.Get(StationEntityField.GhostX), (float)state.Get(StationEntityField.GhostY))
            : null;
        avatar.SetGhost(ghost, state.Id == localPeerId || GhostObserver.IsVisibleTo(localStat));
    }

    /// <summary>Keeps one collision object per solid entity, pushable when the entity allows it.</summary>
    private void SyncEntityObstacles(IEnumerable<StationEntityState> states)
    {
        foreach (StationEntityState state in states)
        {
            if (!state.IsSolid) continue;
            int x = Mathf.RoundToInt(state.Position.X);
            int y = Mathf.RoundToInt(state.Position.Y);
            if (!entityObstacles.TryGetValue(state.Id, out DynamicObject? obstacle))
            {
                obstacle = new DynamicObject(x, y, isPushable: state.IsPushable, tag: state.Kind);
                entityObstacles.Add(state.Id, obstacle);
                collisionMap.AddObject(obstacle);
                continue;
            }
            obstacle.IsPushable = state.IsPushable;
            obstacle.X = x;
            obstacle.Y = y;
        }
    }

    /// <summary>Hands physics-driven displacement (a pushed cart) back to the authoritative world.</summary>
    private void SyncPushedEntities()
    {
        foreach ((int id, DynamicObject obstacle) in entityObstacles)
        {
            System.Numerics.Vector2 known = Entity(id).Position;
            if (obstacle.X != Mathf.RoundToInt(known.X) || obstacle.Y != Mathf.RoundToInt(known.Y))
                sharedInteractionWorld.SetEntityPosition(id, new System.Numerics.Vector2(obstacle.X, obstacle.Y));
        }
    }

    private void ObserveSmokePowerOnHost()
    {
        StationEntityState generator = Entity(StationEntityCatalog.FuelGeneratorId);
        StationEntityState storage = Entity(StationEntityCatalog.PowerStorageId);
        bool powered = Entity(StationEntityCatalog.ApcLampId).Flag(StationEntityField.Powered);
        double storedEnergy = storage.Get(StationEntityField.StoredEnergy);
        if (powered && !smokePowerHostSawOn)
        {
            smokePowerHostSawOn = true;
            GD.Print($"PLAYTEST_POWER_HOST_ON fuel={generator.Get(StationEntityField.FuelSeconds):0.0} energy={storedEnergy:0.0}");
        }
        if (!powered && storage.Flag(StationEntityField.OutputEnabled) && storedEnergy <= 0 &&
            smokePowerHostSawOn && !generator.Flag(StationEntityField.Running) && !smokePowerHostSawOutage)
        {
            smokePowerHostSawOutage = true;
            GD.Print("PLAYTEST_POWER_HOST_OUTAGE energy=0");
        }
        if (powered && smokePowerHostSawOutage && !smokePowerSawRecovery)
        {
            smokePowerSawRecovery = true;
            GD.Print("PLAYTEST_POWER_HOST_RECOVERED shared=true");
        }
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "PlaytestHud" };
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(14, 14) };
        layer.AddChild(panel);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        panel.AddChild(content);
        var title = new Label { Text = "GODOTSTATION · CREW TEST" };
        title.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(title);
        statusLabel = new Label { Text = connectionStatus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        statusLabel.CustomMinimumSize = new Vector2(320, 34);
        content.AddChild(statusLabel);
        startShiftButton = new Button { Text = "Start shift", Visible = false, Disabled = true };
        startShiftButton.Pressed += StartShift;
        content.AddChild(startShiftButton);
        endShiftButton = new Button { Text = "End shift", Visible = false };
        endShiftButton.Pressed += EndShift;
        content.AddChild(endShiftButton);
        restartShiftButton = new Button { Text = "Restart shift", Visible = false };
        restartShiftButton.Pressed += RestartShift;
        content.AddChild(restartShiftButton);
        content.AddChild(new Label
        {
            Text = $"WASD move · LAN UDP {PlaytestLaunchOptions.Port}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        activeHandLabel = new Label { Text = "Active hand: rightHand · empty" };
        content.AddChild(activeHandLabel);
        intentLabel = new Label();
        content.AddChild(intentLabel);
        SetSelectedIntent(selectedIntent);
        healthLabel = new Label { Text = "Health: awaiting role spawn" };
        content.AddChild(healthLabel);
        pullLabel = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(pullLabel);
        loadoutLabel = new Label { Text = "Crew loadout: awaiting role spawn", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        loadoutLabel.CustomMinimumSize = new Vector2(320, 48);
        content.AddChild(loadoutLabel);
        content.AddChild(new Label
        {
            Text = "Task: pick up metal, click girder, stay close 4s. Tab: hand; Q: drop.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        content.AddChild(new Label
        {
            Text = "Engineer: pick up fuel; click generator twice to load it; Tab, click generator to start; click storage output. Watch the APC lamp.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        content.AddChild(new Label
        {
            Text = "Shift-click anything in view to examine it. Ctrl-click a crew member or loose cart to pull it; H lets go.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        content.AddChild(new Label
        {
            Text = "Danger: on harm intent (4) a wrench hits crew for 5 brute, and an empty hand punches for 5 to 10 (kicks for 7 to 15 if they are down). At 0 health crew collapse; untreated they die. Bruise packs in Medical heal 40. Help intent (1) with an empty hand gives a collapsed crew member CPR.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        chatLog = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(320, 48),
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
        };
        content.AddChild(chatLog);
        chatInput = new LineEdit
        {
            PlaceholderText = "Speak locally… (Enter)",
            CustomMinimumSize = new Vector2(320, 32),
            MaxLength = 256,
        };
        chatInput.TextSubmitted += SubmitSpeech;
        content.AddChild(chatInput);

        var returnButton = new Button { Text = "Return to lobby" };
        returnButton.Pressed += () => GetTree().ChangeSceneToFile("res://UI/RoundLobby.tscn");
        content.AddChild(returnButton);

        roundEndReportDialog = new AcceptDialog { Title = "Round report" };
        layer.AddChild(roundEndReportDialog);
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
            string phase = roundLifecycle.Phase switch
            {
                SharedRoundPhase.PreRound => "Pre-round",
                SharedRoundPhase.Started => "Shift started",
                _ => "Shift ended",
            };
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
        /// <summary>False while the body lies on the floor and others may walk over it.</summary>
        public bool ObstacleActive { get; set; } = true;
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
