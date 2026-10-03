using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Renders the active camera to a texture, encodes it as JPG/PNG and sends it as one binary
/// WebSocket frame to the FastAPI /ws/image endpoint. The server answers each frame with one JSON frame.
/// One persistent connection; reconnects automatically if it drops or times out.
/// Attach to any GameObject. Press the capture key, or call Capture() from code/UI.
/// Note: System.Net.WebSockets does not work in WebGL builds (use the NativeWebSocket package there).
/// </summary>
public class ScreenshotUploader : MonoBehaviour
{
	[Header("Server")]
	[SerializeField] string url = "ws://localhost:8000/ws/image";
	[SerializeField] int timeoutSeconds = 10;
	[SerializeField] float reconnectDelaySeconds = 2f;

	[Header("Capture")]
	[Tooltip("Leave empty to use Camera.main")]
	[SerializeField] Camera targetCamera;
	[Tooltip("0 = use the current screen size. The server resizes to 128x72 anyway, so 128 x 72 here saves a lot of work.")]
	[SerializeField] int width = 0;
	[SerializeField] int height = 0;
	[SerializeField] bool usePng = true;
	[SerializeField, Range(1, 100)] int jpgQuality = 90;

	public InputActionReference captureInputAction;
	public GameObject redBall;
	public SkinnedMeshRenderer hands;
	public Animator handsAnimator;

	[Header("Fly Tracking")]
	public Camera mainCamera;
	public Transform flyTransform;
	public Transform topLeft;
	public Transform topCenter;
	public Transform topRight;
	public Transform centerLeft;
	public Transform center;
	public Transform centerRight;
	public Transform bottomLeft;
	public Transform bottomCenter;
	public Transform bottomRight;
	public CheckFlyCollision checkFlyCollision;

	[Header("Hand IK Rig")]
	public Transform leftHandTarget;
	public Transform rightHandTarget;
	float moveDuration = 0.25f;  // seconds, each way // human reaction speed is between 200ms to 280ms
	Vector3 leftRestLocal, rightRestLocal;
	Coroutine leftRoutine, rightRoutine;

	[Header("UI Handling")]
	public UIController uiController;

	// For camera rotation
	float durationScale = 1f;   // seconds
	Coroutine rotateRoutine;
	Dictionary<string, Transform> regionPosition = new Dictionary<string, Transform>();
	int invisibleCount = 0;

	bool padToExactSize = true;
	bool busy;

	// --- WebSocket state
	ClientWebSocket socket;
	CancellationTokenSource lifetimeCts;            // cancelled in OnDestroy so pending awaits stop
	bool connecting;
	float nextConnectTime;
	readonly byte[] receiveBuffer = new byte[16 * 1024];

	[Serializable]
	class ServerError
	{
		public string error;
		public int code;
	}

	void OnEnable()
	{
		captureInputAction.action.started += CaptureKeyAction;
	}

	void OnDisable()
	{
		captureInputAction.action.started -= CaptureKeyAction;
	}

	void Awake()
	{
		// The true rest pose, captured once
		leftRestLocal = leftHandTarget.localPosition;
		rightRestLocal = rightHandTarget.localPosition;

		lifetimeCts = new CancellationTokenSource();
	}

	void OnDestroy()
	{
		lifetimeCts?.Cancel();      // any in-flight connect/send/receive throws and exits quietly
		if (socket != null)
		{
			socket.Abort();         // server sees a disconnect and ends its loop for this client
			socket.Dispose();
			socket = null;
		}
		lifetimeCts?.Dispose();
		lifetimeCts = null;
	}

	static Vector3 ToLocal(Transform t, Vector3 worldPoint)
	{
		return t.parent != null ? t.parent.InverseTransformPoint(worldPoint) : worldPoint;
	}

	private void Start()
	{
		regionPosition["top left"] = topLeft;
		regionPosition["top center"] = topCenter;
		regionPosition["top right"] = topRight;
		regionPosition["center left"] = centerLeft;
		regionPosition["center"] = center;
		regionPosition["center right"] = centerRight;
		regionPosition["bottom left"] = bottomLeft;
		regionPosition["bottom center"] = bottomCenter;
		regionPosition["bottom right"] = bottomRight;

		EnsureConnected();
	}

	private void Update()
	{
		Capture();
	}

	void CaptureKeyAction(InputAction.CallbackContext obj)
	{
		Capture();
	}

	[ContextMenu("Capture and send")]
	public void Capture()
	{
		if (lifetimeCts == null || busy)
		{
			return;     // not playing yet, or one frame already in flight
		}

		if (socket == null || socket.State != WebSocketState.Open)
		{
			EnsureConnected();      // no-op while connecting or during the reconnect delay
			return;
		}

		CaptureAndSend();
	}

	// ---------------------------------------------------------------- WebSocket

	// async void is fine here: started from the main thread, so every continuation
	// after an await runs on the main thread too (Unity's SynchronizationContext).
	async void EnsureConnected()
	{
		if (connecting || lifetimeCts == null || Time.time < nextConnectTime) return;
		if (socket != null && socket.State == WebSocketState.Open) return;

		connecting = true;
		socket?.Dispose();
		socket = new ClientWebSocket();

		try
		{
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token))
			{
				cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
				await socket.ConnectAsync(new Uri(url), cts.Token);
			}
			Debug.Log($"ScreenshotUploader: connected to {url}");
		}
		catch (Exception e)
		{
			if (lifetimeCts != null && !lifetimeCts.IsCancellationRequested)
				Debug.LogWarning($"ScreenshotUploader: connect failed ({e.Message}), retrying in {reconnectDelaySeconds}s");
			nextConnectTime = Time.time + reconnectDelaySeconds;
		}
		finally
		{
			connecting = false;
		}
	}

	async void CaptureAndSend()
	{
		busy = true;
		try
		{
			Camera cam = targetCamera != null ? targetCamera : Camera.main;
			if (cam == null)
			{
				Debug.LogError("ScreenshotUploader: no camera assigned and no Camera.main found.");
				return;
			}

			// Set red ball to active, render camera, and disable the red ball
			redBall.SetActive(true);
			hands.enabled = false;
			byte[] bytes;
			try
			{
				bytes = RenderCamera(cam);
			}
			finally
			{
				redBall.SetActive(false);
				hands.enabled = true;
			}

			// One binary frame out, one JSON text frame back (the server answers strictly in order).
			string reply;
			using (var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token))
			{
				cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
				await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cts.Token);
				reply = await ReceiveTextAsync(cts.Token);
			}

			if (this == null) return;   // destroyed while waiting

			// Errors come back as {"error": "...", "code": 400|413|500} and the connection stays open
			ServerError err = JsonUtility.FromJson<ServerError>(reply);
			if (!string.IsNullOrEmpty(err.error))
			{
				Debug.LogError($"ScreenshotUploader: server error {err.code}: {err.error}");
				return;
			}

			// Reposition Camera to face the fly, or kill the fly
			ProcessResponse(reply);
		}
		catch (Exception e)
		{
			// A timeout/cancel leaves the socket Aborted; the next Capture() reconnects.
			if (lifetimeCts != null && !lifetimeCts.IsCancellationRequested)
			{
				string why = e is OperationCanceledException ? "timed out" : e.Message;
				Debug.LogError($"ScreenshotUploader: {why}");
				nextConnectTime = Time.time + reconnectDelaySeconds;
			}
		}
		finally
		{
			busy = false;
		}
	}

	// A message can arrive split across several frames; read until EndOfMessage.
	async Task<string> ReceiveTextAsync(CancellationToken ct)
	{
		using (var ms = new MemoryStream())
		{
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), ct);
				if (result.MessageType == WebSocketMessageType.Close)
					throw new WebSocketException("server closed the connection");
				ms.Write(receiveBuffer, 0, result.Count);
			}
			while (!result.EndOfMessage);

			return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
		}
	}

	// ---------------------------------------------------------------- Capture

	byte[] RenderCamera(Camera cam)
	{
		int viewW = cam.pixelWidth; // camera's viewport on screen, in pixels
		int viewH = cam.pixelHeight;
		float viewAspect = (float)viewW / viewH;

		// Output box.
		int boxW, boxH;
		if (width > 0 && height > 0) { boxW = width; boxH = height; }
		else if (width > 0) { boxW = width; boxH = Mathf.Max(1, Mathf.RoundToInt(width / viewAspect)); }
		else if (height > 0) { boxH = height; boxW = Mathf.Max(1, Mathf.RoundToInt(height * viewAspect)); }
		else { boxW = viewW; boxH = viewH; }

		// Largest size with the camera's aspect that fits inside the box.
		int renderW, renderH;
		if ((float)boxW / boxH > viewAspect) { renderH = boxH; renderW = Mathf.Max(1, Mathf.RoundToInt(boxH * viewAspect)); }
		else { renderW = boxW; renderH = Mathf.Max(1, Mathf.RoundToInt(boxW / viewAspect)); }

		int outW = padToExactSize ? boxW : renderW;
		int outH = padToExactSize ? boxH : renderH;
		int offsetX = (outW - renderW) / 2;
		int offsetY = (outH - renderH) / 2;

		// Render at the camera's real on-screen size (the case that already works), then shrink on the GPU.
		RenderTexture full = RenderTexture.GetTemporary(viewW, viewH, 24);
		full.filterMode = FilterMode.Bilinear;
		var temps = new List<RenderTexture>();

		RenderTexture prevTarget = cam.targetTexture;
		RenderTexture prevActive = RenderTexture.active;
		Rect prevRect = cam.rect;

		try
		{
			cam.rect = new Rect(0f, 0f, 1f, 1f);
			cam.targetTexture = full;
			cam.Render();

			// Halve while still >= 2x the target: a bilinear sample between 2x2 texels = their average.
			RenderTexture cur = full;
			while (cur.width / 2 >= renderW && cur.height / 2 >= renderH)
			{
				RenderTexture half = RenderTexture.GetTemporary(cur.width / 2, cur.height / 2, 0);
				half.filterMode = FilterMode.Bilinear;
				Graphics.Blit(cur, half);
				temps.Add(half);
				cur = half;
			}

			// Final step to the exact size (less than 2x reduction, so bilinear doesn't skip pixels).
			if (cur.width != renderW || cur.height != renderH)
			{
				RenderTexture exact = RenderTexture.GetTemporary(renderW, renderH, 0);
				Graphics.Blit(cur, exact);
				temps.Add(exact);
				cur = exact;
			}

			RenderTexture.active = cur;
			var tex = new Texture2D(outW, outH, TextureFormat.RGB24, false);
			if (outW != renderW || outH != renderH)
				tex.SetPixels32(new Color32[outW * outH]);  // black bars
			tex.ReadPixels(new Rect(0, 0, renderW, renderH), offsetX, offsetY);
			tex.Apply();

			byte[] bytes = usePng ? tex.EncodeToPNG() : tex.EncodeToJPG(jpgQuality);
			Destroy(tex);                                   // avoid leaking a texture per capture
			return bytes;
		}
		finally
		{
			cam.rect = prevRect;
			cam.targetTexture = prevTarget;
			RenderTexture.active = prevActive;
			foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
			RenderTexture.ReleaseTemporary(full);
		}
	}

	// ---------------------------------------------------------------- Response handling (unchanged)

	void ProcessResponse(string jsonText)
	{
		JevData jevData = JsonUtility.FromJson<JevData>(jsonText);
		uiController.updateScriptableObject(jevData);

		Transform lookTarget = regionPosition[jevData.positions[0].label];

		//// if the red ball isn't visible -> try to look for it in random directions
		if (jevData.visibles[0].label == "no")
		{
			invisibleCount += 1;
		}

		// Stop any rotation already in progress so calls don't fight each other
		if (invisibleCount >= 10)
		{
			if (rotateRoutine != null) StopCoroutine(rotateRoutine);
			Debug.Log("reset to look at the target");
			RotateYawPitchToward_NoLerp(flyTransform);
			invisibleCount = 0;
		}
		if (rotateRoutine != null)
		{
			StopCoroutine(rotateRoutine);
			rotateRoutine = null;
		}
		rotateRoutine = StartCoroutine(RotateToLookAt(lookTarget, 0.3f));

		// Firing State
		if (jevData.visibles[0].label == "yes"
			&& jevData.visibles[0].probs > 0.8
			&& jevData.positions[0].probs > 0.6
			&& (jevData.positions[0].label == "center" || jevData.positions[0].label == "center right" || jevData.positions[0].label == "center left"))
		{
			switch (jevData.distances[0].label)
			{
				case "very close":
					handsAnimator.Play("Clap_VeryClose");
					break;
				case "close":
					handsAnimator.Play("Clap_Close");
					break;
				case "medium range":
					handsAnimator.Play("Clap_MediumRange");
					break;
				case "far":
					handsAnimator.Play("Clap_Far");
					break;
				case "very far":
					handsAnimator.Play("Clap_VeryFar");
					break;
			}

			Debug.LogError("KILL: " + jsonText);
			// KIll The fly
			// ReachBoth(flyTransform, flyTransform);
		}
	}

	// Rotation LERP
	IEnumerator RotateToLookAt(Transform target, float durationMultiplier)
	{
		Transform cam = mainCamera.transform;
		Quaternion startRot = cam.rotation;
		Quaternion endRot = Quaternion.LookRotation(target.position - cam.position);
		float newDuration = durationScale * durationMultiplier;
		//Debug.Log("Rotate duration: " + newDuration);

		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / newDuration;
			cam.rotation = Quaternion.Slerp(startRot, endRot, Mathf.SmoothStep(0f, 1f, t));
			yield return null;
		}
		cam.rotation = endRot;   // snap exactly to the final rotation
		rotateRoutine = null;
	}

	IEnumerator RotateYawPitchToward(Transform target, float durationScale, float amount = 1f)
	{
		Transform cam = mainCamera.transform;
		Vector3 startEuler = cam.eulerAngles;

		// Direction to target, flattened onto the horizontal plane
		Vector3 dir = target.position - cam.position;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.0001f) yield break;   // target is directly above/below: no yaw to compute

		float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
		float endYaw = Mathf.LerpAngle(startEuler.y, targetYaw, amount);  // amount < 1 = turn only partway
		float endPitch = 0f;

		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / durationScale;
			float s = Mathf.SmoothStep(0f, 1f, t);
			float yaw = Mathf.LerpAngle(startEuler.y, endYaw, s);
			float pitch = Mathf.LerpAngle(startEuler.x, endPitch, s);
			cam.rotation = Quaternion.Euler(pitch, yaw, startEuler.z);
			yield return null;
		}

		cam.rotation = Quaternion.Euler(startEuler.x, endYaw, startEuler.z);
		rotateRoutine = null;
	}

	void RotateYawPitchToward_NoLerp(Transform target, float amount = 1f)
	{
		Transform cam = mainCamera.transform;
		Vector3 startEuler = cam.eulerAngles;

		// Direction to target, flattened onto the horizontal plane
		Vector3 dir = target.position - cam.position;
		dir.y = 0f;

		// If the target is directly above/below, keep the current yaw but still level the pitch
		float yaw = startEuler.y;
		if (dir.sqrMagnitude > 0.0001f)
		{
			float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
			yaw = Mathf.LerpAngle(startEuler.y, targetYaw, amount);  // amount < 1 = turn only partway
		}

		cam.rotation = Quaternion.Euler(0f, yaw, startEuler.z);
		Debug.Log("Rotation has been reset");
	}

	// Hand Movement LERP
	public void ReachBoth(Transform leftPoint, Transform rightPoint)
	{
		ReachLeft(leftPoint);
		ReachRight(rightPoint);
	}

	public void ReachLeft(Transform point)
	{
		if (leftRoutine != null) StopCoroutine(leftRoutine);
		leftRoutine = StartCoroutine(ReachAndReturn(leftHandTarget, leftRestLocal, point));
	}

	public void ReachRight(Transform point)
	{
		if (rightRoutine != null) StopCoroutine(rightRoutine);
		rightRoutine = StartCoroutine(ReachAndReturn(rightHandTarget, rightRestLocal, point));
	}

	IEnumerator ReachAndReturn(Transform ikTarget, Vector3 restLocal, Transform point)
	{
		// 1. Out: blend from wherever the hand is now toward the point.
		//    The goal is recomputed every frame, so it stays correct if the
		//    character or the point moves during the reach.
		Vector3 startLocal = ikTarget.localPosition;
		float t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / moveDuration;
			Vector3 goalLocal = ToLocal(ikTarget, point.position);
			ikTarget.localPosition = Vector3.Lerp(startLocal, goalLocal, Mathf.SmoothStep(0f, 1f, t));
			yield return null;
		}

		// 2. Hold: stay pinned to the point
		//float held = 0f;
		//while (held < holdTime)
		//{
		//	held += Time.deltaTime;
		//	ikTarget.position = point.position;
		//	yield return null;
		//}
		checkFlyCollision.AnimeTrigger();

		// 3. Back: return to the rest localPosition
		startLocal = ikTarget.localPosition;
		t = 0f;
		while (t < 1f)
		{
			t += Time.deltaTime / moveDuration;
			ikTarget.localPosition = Vector3.Lerp(startLocal, restLocal, Mathf.SmoothStep(0f, 1f, t));
			yield return null;
		}
		ikTarget.localPosition = restLocal;
	}
}