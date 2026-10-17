using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Godot;
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
    private CollisionMap collisionMap = null!;
    private MovementProcessor movementProcessor = null!;
    private Label statusLabel = null!;
    private int localPeerId;
    private float snapshotTime;
    private ulong smokeMoveUntilMsec;
    private bool smokeMoveStarted;
    private string connectionStatus = "Connecting…";
    private string hostAddress = "LAN IP unavailable";

    public override void _Ready()
    {
        SetMultiplayerAuthority(1);
        collisionMap = StationPlaytestLayout.CreateCollisionMap();
        movementProcessor = new MovementProcessor(collisionMap);
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
        var direction = ReadLocalInput();

        if (multiplayer.IsServer())
        {
            if (players.ContainsKey(localPeerId))
                SetMovementInput(localPeerId, localPeerId, direction.X, direction.Y);

            float step = (float)Math.Min(delta, 0.1);
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
        CrewAssignmentResult assignment = crewRoster.TryAssign(localPeerId, PlaytestLaunchOptions.CrewJobId);
        if (assignment != CrewAssignmentResult.Accepted ||
            !crewRoster.TryGetAssignment(localPeerId, out CrewJob? assignedJob) || assignedJob == null)
        {
            SetStatus($"Could not reserve the selected crew role: {assignment}.");
            peer.Close();
            return;
        }
        var spawn = StationPlaytestLayout.Spawns[0];
        AddPlayer(localPeerId, assignedJob, new Vector2(spawn.X, spawn.Y));
        hostAddress = GetLocalIpv4Address();
        SetStatus($"Hosting · join {hostAddress}:{PlaytestLaunchOptions.Port} · UDP");
        GD.Print($"PLAYTEST_HOST_READY port={PlaytestLaunchOptions.Port} job={assignedJob.Id}");
        RefreshStatus();
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
        SetStatus($"Connected · waiting for crew spawn (peer {localPeerId})");
        GD.Print($"PLAYTEST_CONNECTED local_peer={localPeerId}");
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
        Vector2 spawn = new(spawnPoint.X, spawnPoint.Y);
        foreach (PlayerRuntime existing in players.Values)
            RpcId(peerId, nameof(ReceiveSpawn), existing.PeerId, existing.Job.Id,
                existing.Position.X, existing.Position.Y);

        AddPlayer(peerId, assignedJob, spawn);
        Rpc(nameof(ReceiveSpawn), peerId, assignedJob.Id, spawn.X, spawn.Y);
        GD.Print($"PLAYTEST_ROSTER peers={players.Count}");
        RefreshStatus();
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
        AddChild(new StationMapView());
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
        content.AddChild(new Label { Text = $"WASD move  ·  Host shares LAN IP  ·  UDP {PlaytestLaunchOptions.Port}" });

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
            connectionStatus = $"Hosting · join {hostAddress}:{PlaytestLaunchOptions.Port} · crew {players.Count}/{MaximumPlayers}";
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
}
