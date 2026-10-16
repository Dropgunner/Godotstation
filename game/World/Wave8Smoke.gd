extends Node

func _ready() -> void:
	call_deferred("run_smoke")

func run_smoke() -> void:
	await get_tree().process_frame
	var lobby := get_tree().current_scene
	var job_button := find_button(lobby, "Station Engineer")
	var start_button := find_button(lobby, "Start Round")
	assert(job_button != null and start_button != null, "Job choice controls were not built")
	job_button.emit_signal("pressed")
	start_button.emit_signal("pressed")
	await wait_for_scene()

	var station := get_tree().current_scene
	assert(station.scene_file_path == "res://World/Wave8Station.tscn", "Station scene was not entered")
	var round = get_tree().root.get_node("Round")
	assert(round.OutcomeName == "InProgress", "Round did not start")
	assert(get_tree().root.get_node("Lobby").SelectedJobName == "Station Engineer",
		"Selected job was not carried into the station round")
	var player := station.get_node("Systems/Player")
	var targets := [station.get_node("Systems/Pipe"), station.get_node("RelayTwo"), station.get_node("RelayThree")]
	for target in targets:
		assert(player.TryInteractWith(target, target.global_position, false),
			"Wrench interaction was rejected for %s" % target.name)
		await get_tree().process_frame
	assert(round.ObjectiveProgress == 3, "Wrench clicks did not complete the objective")
	assert(round.OutcomeName == "Success", "Objective completion did not resolve success")

	await restart_from_result(station)
	lobby = get_tree().current_scene
	await start_selected_job(lobby)
	station = get_tree().current_scene
	assert(round.ObjectiveProgress == 0 and round.TimeRemainingSeconds == 90,
		"Restart after success retained state")
	round.AdvanceTime(90)
	assert(round.OutcomeName == "Failure", "Timer expiration did not resolve failure")

	await restart_from_result(station)
	lobby = get_tree().current_scene
	await start_selected_job(lobby)
	assert(round.OutcomeName == "InProgress" and round.ObjectiveProgress == 0
		and round.TimeRemainingSeconds == 90, "Restart after failure was not fresh")
	print("WAVE8_SMOKE_OK: job selection, objective success, timeout failure, and fresh restarts")
	get_tree().quit(0)

func start_selected_job(lobby: Node) -> void:
	find_button(lobby, "Station Engineer").emit_signal("pressed")
	find_button(lobby, "Start Round").emit_signal("pressed")
	await wait_for_scene()

func restart_from_result(station: Node) -> void:
	var restart := find_button(station, "Return to Job Selection")
	assert(restart != null and restart.visible, "Result screen has no restart button")
	restart.emit_signal("pressed")
	await wait_for_scene()

func wait_for_scene() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().process_frame

func find_button(parent: Node, matching_text: String) -> Button:
	for candidate in parent.find_children("*", "Button", true, false):
		if candidate.text.begins_with(matching_text):
			return candidate
	return null

