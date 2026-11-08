using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using PajamaLlama.Math;

namespace FlotsamTrainer
{
    public sealed partial class TrainerPanel
    {
        private static readonly FieldInfo MapTransition =
            AccessTools.Field(typeof(WorldMap), "_isChangingState");
        private static readonly FieldInfo DefaultFogRange =
            AccessTools.Field(typeof(WorldMap), "_defaultFOWRange");
        private static readonly FieldInfo CameraRendererIndex =
            AccessTools.Field(typeof(UniversalAdditionalCameraData), "m_RendererIndex");
        private static readonly FieldInfo MapPhysics =
            AccessTools.Field(typeof(WorldMap), "_townheartPhysics");
        private static readonly FieldInfo TownheartBody =
            AccessTools.Field(typeof(WorldMapTownheartPhycis2D), "_rigidbody");
        private static readonly FieldInfo PreviousPhysicsPosition =
            AccessTools.Field(typeof(WorldMapTownheartPhycis2D), "_position");
        private static readonly Action<WorldMapTownheart, Vector3, Quaternion> SetTownheartVisualPose =
            (Action<WorldMapTownheart, Vector3, Quaternion>)AccessTools.Method(typeof(WorldMapTownheart), "SetPositionAndRotation")
                .CreateDelegate(typeof(Action<WorldMapTownheart, Vector3, Quaternion>));
        private static readonly Action<WorldMap> UpdateMapTiles =
            (Action<WorldMap>)AccessTools.Method(typeof(WorldMap), "LateUpdate").CreateDelegate(typeof(Action<WorldMap>));
        private static readonly Action<WorldMap> UpdateMapScouting =
            (Action<WorldMap>)AccessTools.Method(typeof(WorldMap), "UpdateScouting").CreateDelegate(typeof(Action<WorldMap>));
        private static readonly Action<WorldMap> UpdateMapShader =
            (Action<WorldMap>)AccessTools.Method(typeof(WorldMap), "UpdateGlobalShaderVariables").CreateDelegate(typeof(Action<WorldMap>));
        private static readonly Action<WorldMap> PruneMapTiles =
            (Action<WorldMap>)AccessTools.Method(typeof(WorldMap), "PruneWorldtiles").CreateDelegate(typeof(Action<WorldMap>));
        private static readonly Func<WorldMap, LandmarkMapObstacle> ClosestObstacle =
            (Func<WorldMap, LandmarkMapObstacle>)AccessTools.Method(typeof(WorldMap), "ReturnClosestObstacle")
                .CreateDelegate(typeof(Func<WorldMap, LandmarkMapObstacle>));
        private static readonly Action<WorldMapTownheartPhycis2D, float, float> StopVelocity =
            (Action<WorldMapTownheartPhycis2D, float, float>)AccessTools.Method(typeof(WorldMapTownheartPhycis2D), "SetVelocity")
                .CreateDelegate(typeof(Action<WorldMapTownheartPhycis2D, float, float>));

        private bool showMinimap;
        private bool quickSailing;
        private RectTransform minimap;
        private Camera minimapCamera;
        private RenderTexture minimapTexture;
        private WorldMap backgroundMap;
        private WorldMapTownheartPhycis2D physics;
        private bool mapComponentEnabled;
        private bool mapCameraEnabled;
        private bool mapControllerEnabled;
        private bool mapZoomEnabled;
        private bool mapPhysicsEnabled;
        private bool sailing;
        private MinimapInput minimapInput;
        private int rightMouseFrame = -1;
        private bool minimapOwnsRightMouse;
        private bool showMinimapFog;
        private Text minimapFogLabel;
        private float nextMinimapFogScan;
        private MinimapFogOverlay minimapFogOverlay;
        private readonly Dictionary<WorldMapFogOfWar, (MeshRenderer Renderer, bool WasHidden)> minimapFogRenderers =
            new Dictionary<WorldMapFogOfWar, (MeshRenderer, bool)>();
        private Quaternion sailRotation;
        private Vector3 lastSailPosition;
        private Vector3 committedMapPosition;
        private Quaternion committedMapRotation;
        private float sailDistance;
        private float nextMapUpdate;
        private float mapZoom = 215f;
        private Vector2 minimapOffset;
        private MouseMovementCursorProperties.Gear sailGear;

        private void CreateMinimap()
        {
            var box = new GameObject("Minimap", typeof(RectTransform));
            minimap = box.GetComponent<RectTransform>();
            var canvas = (RectTransform)transform;
            Canvas.ForceUpdateCanvases();
            Position(minimap, canvas, new Rect(Mathf.Max(12f, canvas.rect.width - 296f), 104, 278, 304));
            minimap.SetAsFirstSibling();
            UseSlicedArt(AddImage(minimap, new Rect(0, 0, 278, 304), PanelColor), frameArt);
            var title = AddImage(minimap, new Rect(8, 7, 262, 27), TitleColor);
            UseSlicedArt(title, controlArt);
            Label(minimap, "航海图", new Rect(18, 7, 100, 27), 17, Ink);
            BindGrip(title, minimap, false);
            var fogButton = AddButton(minimap, "迷雾 关", new Rect(76, 8, 69, 25), ButtonColor, ToggleMinimapFog);
            minimapFogLabel = fogButton.GetComponentInChildren<Text>();
            AddButton(minimap, "回到自身", new Rect(147, 8, 119, 25), ButtonColor, CenterMinimap);

            minimapTexture = new RenderTexture(384, 384, 24) { name = "Flotsam minimap" };
            var picture = new GameObject("Map view", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Position(picture.rectTransform, minimap, new Rect(14, 39, 250, 250));
            picture.texture = minimapTexture;
            minimapInput = picture.gameObject.AddComponent<MinimapInput>();
            minimapInput.Zoom = delta => mapZoom = Mathf.Clamp(mapZoom * Mathf.Pow(0.85f, delta), 75f, 1450f);
            minimapInput.Pan = delta => minimapOffset -= delta * (2f * mapZoom / picture.rectTransform.rect.height);
            var fogImage = new GameObject("Minimap fog", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            Position(fogImage.rectTransform, minimap, new Rect(14, 39, 250, 250));
            fogImage.raycastTarget = false;
            minimapFogOverlay = fogImage.gameObject.AddComponent<MinimapFogOverlay>();
            fogImage.gameObject.SetActive(false);

            AddCompassPoint("北", 127, 42);
            AddCompassPoint("南", 127, 262);
            AddCompassPoint("西", 14, 151);
            AddCompassPoint("东", 240, 151);
            var handle = AddImage(minimap, new Rect(246, 274, 27, 26), ButtonColor);
            UseSlicedArt(handle, controlArt);
            Label(minimap, "◢", new Rect(246, 274, 27, 26), 16, Ink, TextAnchor.MiddleCenter);
            BindGrip(handle, minimap, true);
            box.SetActive(false);

            var cameraHost = new GameObject("Minimap camera");
            cameraHost.transform.SetParent(transform, false);
            minimapCamera = cameraHost.AddComponent<Camera>();
            minimapCamera.enabled = false;
        }

        private void AddCompassPoint(string caption, int x, int y)
        {
            var bounds = new Rect(x, y, 24, 23);
            var plate = AddImage(minimap, bounds, FieldColor);
            UseSlicedArt(plate, controlArt);
            plate.raycastTarget = false;
            Label(minimap, caption, bounds, 14, Ink, TextAnchor.MiddleCenter);
        }

        private void BindGrip(Image image, RectTransform target, bool resize)
        {
            var grip = image.gameObject.AddComponent<PanelGrip>();
            grip.Window = target;
            grip.Canvas = (RectTransform)transform;
            grip.Resize = resize;
        }

        private bool CanSail => showMinimap && quickSailing && Community.PlayerCommunity?.Engine != null &&
            UIManager.State == UIState.Normal && !LoadingScreen.IsLoading && !GameManager.Gamepaused;

        private void CenterMinimap() => minimapOffset = Vector2.zero;

        private void ToggleMinimapFog()
        {
            showMinimapFog = !showMinimapFog;
            minimapFogLabel.text = showMinimapFog ? "迷雾 开" : "迷雾 关";
            minimapFogOverlay.gameObject.SetActive(showMinimapFog);
            minimapFogOverlay.Invalidate();
            nextMinimapFogScan = 0f;
            if (showMinimapFog)
            {
                if (backgroundMap != null)
                {
                    UpdateMapScouting(backgroundMap);
                    float radius = WatchTower.Enabled ? backgroundMap.ReturnTileSpawningRange() : (float)DefaultFogRange.GetValue(backgroundMap);
                    mapZoom = Mathf.Max(mapZoom, radius * 1.3f);
                }
            }
        }

        private void UpdateMinimapFog(WorldMap map)
        {
            if (Time.unscaledTime < nextMinimapFogScan)
                return;

            nextMinimapFogScan = Time.unscaledTime + 0.25f;
            foreach (var fog in map.GetComponentsInChildren<WorldMapFogOfWar>(true))
            {
                var renderer = fog.GetComponent<MeshRenderer>();
                if (renderer == null || minimapFogRenderers.ContainsKey(fog))
                    continue;
                minimapFogRenderers.Add(fog, (renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
                minimapFogOverlay.Invalidate();
            }
        }

        private void RestoreMinimapFog()
        {
            foreach (var state in minimapFogRenderers.Values)
                if (state.Renderer != null)
                    state.Renderer.forceRenderingOff = state.WasHidden;
            minimapFogRenderers.Clear();
            nextMinimapFogScan = 0f;
            if (minimapFogOverlay != null)
                minimapFogOverlay.Invalidate();
        }

        private void UpdateNavigation()
        {
            var map = GameManager.WorldMapManager?.WorldMap;
            var world = GameManager.WorldManager?.World;
            bool ready = map != null && world != null && map.Townheart != null && map.Townheart.Initialized &&
                !LoadingScreen.IsLoading && !(bool)MapTransition.GetValue(map) && UIManager.State != UIState.Map;
            if (!ready || !showMinimap || backgroundMap != null && backgroundMap != map)
                ReleaseBackgroundMap();
            if (!ready || !showMinimap)
                return;

            if (backgroundMap == null && !AcquireBackgroundMap(map))
                return;

            if (CanSail && minimapInput.Held && Input.GetMouseButton(0) && TrySailDirection(out var direction))
            {
                if (!sailing)
                    StartSailing();
                if (!sailing)
                    return;
                if (sailGear == MouseMovementCursorProperties.Gear.Neutral)
                    sailGear = Vector3.Angle(sailRotation * Vector3.forward, direction) <= 120f
                        ? MouseMovementCursorProperties.Gear.Forward : MouseMovementCursorProperties.Gear.Reverse;
                physics.SetMouseMovementTargetAndDirection(physics.Position + direction * 150f, sailGear);
            }
            else
            {
                FinishSailing();
            }

            bool visible = showMinimap && UIManager.State != UIState.Paused;
            minimap.gameObject.SetActive(visible);
            minimapCamera.enabled = visible;
            if (visible)
            {
                UpdateMinimapFog(map);
                var center = map.Townheart.Position + new Vector3(minimapOffset.x, 0f, minimapOffset.y);
                minimapCamera.transform.SetPositionAndRotation(center + Vector3.up * 650f, Quaternion.Euler(90f, 0f, 0f));
                minimapCamera.orthographicSize = mapZoom;
                if (showMinimapFog)
                    minimapFogOverlay.Refresh(minimapCamera, minimapFogRenderers.Keys);
            }
        }

        private bool MinimapOwnsRightMouse()
        {
            if (rightMouseFrame == Time.frameCount)
                return minimapOwnsRightMouse;

            rightMouseFrame = Time.frameCount;
            if (Input.GetMouseButtonDown(1))
            {
                minimapOwnsRightMouse = false;
                var events = EventSystem.current;
                if (minimapInput != null && minimapInput.isActiveAndEnabled && events != null)
                {
                    var hits = new List<RaycastResult>();
                    events.RaycastAll(new PointerEventData(events) { position = Input.mousePosition }, hits);
                    minimapOwnsRightMouse = hits.Count > 0 && hits[0].gameObject == minimapInput.gameObject;
                }
            }
            else if (!Input.GetMouseButton(1) && !Input.GetMouseButtonUp(1))
            {
                minimapOwnsRightMouse = false;
            }

            return minimapOwnsRightMouse;
        }

        [HarmonyPatch(typeof(CameraControllerGrab), nameof(CameraControllerGrab.GetMovement))]
        private static class RouteMinimapCameraGrab
        {
            private static bool Prefix(ref Vector3 __result)
            {
                if (instance == null || !instance.MinimapOwnsRightMouse())
                    return true;

                __result = Vector3.zero;
                return false;
            }
        }

        [HarmonyPatch(typeof(CameraControllerGrab), nameof(CameraControllerGrab.TryGetFocusPosition))]
        private static class RouteMinimapCameraFocus
        {
            private static bool Prefix(ref Vector3 position, ref bool __result)
            {
                if (instance == null || !instance.MinimapOwnsRightMouse())
                    return true;

                position = Vector3.zero;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(FlotsamInputManager), "Internal_GetCameraInput")]
        private static class RouteMinimapCameraRotation
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var button = AccessTools.Method(typeof(FlotsamInputManager), nameof(FlotsamInputManager.GetButton));
                var filter = AccessTools.Method(typeof(RouteMinimapCameraRotation), nameof(FilterMouseRotation));
                var matcher = new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(instruction => instruction.Calls(button)));
                return matcher.ThrowIfInvalid("Camera mouse rotation input was not found.")
                    .Advance(1).Insert(new CodeInstruction(OpCodes.Call, filter)).InstructionEnumeration();
            }

            private static bool FilterMouseRotation(bool pressed) =>
                pressed && (instance == null || !instance.MinimapOwnsRightMouse());
        }

        [HarmonyPatch(typeof(FlotsamInputManager), nameof(FlotsamInputManager.GetMouseUICancel))]
        private static class RouteMinimapMouseCancel
        {
            private static bool Prefix(ref bool __result)
            {
                if (instance == null || !instance.MinimapOwnsRightMouse())
                    return true;

                __result = false;
                return false;
            }
        }

        private bool AcquireBackgroundMap(WorldMap map)
        {
            var controller = map.WorldCameraController;
            if (map.gameObject.activeSelf || controller == null || controller.Camera == null)
                return false;
            var body = (WorldMapTownheartPhycis2D)MapPhysics.GetValue(map);
            if (body == null)
                return false;

            backgroundMap = map;
            physics = body;
            mapComponentEnabled = map.enabled;
            mapControllerEnabled = controller.enabled;
            mapCameraEnabled = controller.Camera.enabled;
            mapPhysicsEnabled = physics.enabled;
            var zoom = controller.ZoomController;
            mapZoomEnabled = zoom != null && zoom.enabled;
            map.enabled = false;
            physics.enabled = false;
            controller.enabled = false;
            controller.Camera.enabled = false;
            if (zoom != null)
                zoom.enabled = false;

            minimapCamera.CopyFrom(controller.Camera);
            if (controller.Camera.TryGetComponent<UniversalAdditionalCameraData>(out var sourceCameraData))
            {
                var minimapData = minimapCamera.GetUniversalAdditionalCameraData();
                minimapData.SetRenderer((int)CameraRendererIndex.GetValue(sourceCameraData));
                minimapData.requiresDepthOption = sourceCameraData.requiresDepthOption;
                minimapData.requiresColorOption = sourceCameraData.requiresColorOption;
                minimapData.volumeLayerMask = sourceCameraData.volumeLayerMask;
                minimapData.volumeTrigger = sourceCameraData.volumeTrigger;
                minimapData.renderPostProcessing = sourceCameraData.renderPostProcessing;
            }
            minimapCamera.enabled = false;
            minimapCamera.targetTexture = minimapTexture;
            minimapCamera.rect = new Rect(0, 0, 1, 1);
            minimapCamera.aspect = 1f;
            minimapCamera.orthographic = true;
            minimapCamera.nearClipPlane = 0.5f;
            minimapCamera.farClipPlane = 1800f;
            minimapCamera.allowHDR = false;
            minimapCamera.allowMSAA = false;
            map.AddMovementBlocker(map);
            map.gameObject.SetActive(true);
            UpdateMapShader(map);
            UpdateMapScouting(map);
            return true;
        }

        private void ReleaseBackgroundMap()
        {
            RestoreMinimapFog();
            if (minimap != null)
                minimap.gameObject.SetActive(false);
            if (minimapCamera != null)
                minimapCamera.enabled = false;
            if (backgroundMap == null)
            {
                sailing = false;
                return;
            }

            var map = backgroundMap;
            try
            {
                FinishSailing();
            }
            finally
            {
                backgroundMap = null;
                map.gameObject.SetActive(false);
                map.enabled = mapComponentEnabled;
                var controller = map.WorldCameraController;
                controller.enabled = mapControllerEnabled;
                controller.Camera.enabled = mapCameraEnabled;
                if (controller.ZoomController != null)
                    controller.ZoomController.enabled = mapZoomEnabled;
                if (physics != null)
                    physics.enabled = mapPhysicsEnabled;
                physics = null;
            }
        }

        private bool TrySailDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            var view = (RectTransform)minimapInput.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(view, Input.mousePosition, null, out var point) ||
                !view.rect.Contains(point))
                return false;

            var offset = minimapOffset + (point - view.rect.center) * (2f * mapZoom / view.rect.height);
            if (offset.sqrMagnitude < 4f)
                return false;

            // The minimap camera is fixed north-up. Screen right is east and screen up is north.
            direction = new Vector3(offset.x, 0f, offset.y).normalized;
            return true;
        }

        private void StartSailing()
        {
            backgroundMap.RemoveMovementBlocker(backgroundMap);
            if (backgroundMap.IsTownMovementBlocked)
            {
                backgroundMap.AddMovementBlocker(backgroundMap);
                return;
            }

            lastSailPosition = committedMapPosition = backgroundMap.Townheart.Position;
            sailRotation = committedMapRotation = backgroundMap.Townheart.transform.rotation;
            sailDistance = 0f;
            sailGear = MouseMovementCursorProperties.Gear.Neutral;
            nextMapUpdate = 0f;
            physics.enabled = true;
            ApplyCommittedMapPose();
            sailing = true;
            backgroundMap.Townheart.OnStartMove();
            MovementEvent.DispatchStartedMoving();
        }

        private void ApplyCommittedMapPose()
        {
            // The map is XZ, while the game's Rigidbody2D moves in XY and rotates around Z.
            var body = (Rigidbody2D)TownheartBody.GetValue(physics);
            var position = committedMapPosition.Vector2TopDown();
            body.position = position;
            body.rotation = -committedMapRotation.eulerAngles.y;
            PreviousPhysicsPosition.SetValue(physics, position);

            var townheart = backgroundMap.Townheart;
            townheart.SetTargetPositionAndRotation(committedMapPosition, committedMapRotation);
            SetTownheartVisualPose(townheart, committedMapPosition, committedMapRotation);
        }

        private void MeasureSailing()
        {
            var position = physics.Position;
            float distance = Vector3.Distance(lastSailPosition, position);
            lastSailPosition = position;
            if (distance > 0f)
            {
                sailDistance += distance;
                Community.PlayerCommunity.Engine.ConsumeEnergy(distance);
            }
        }

        private void LateUpdate()
        {
            if (!sailing || backgroundMap == null || physics == null)
                return;

            MeasureSailing();
            if (Time.unscaledTime >= nextMapUpdate)
            {
                nextMapUpdate = Time.unscaledTime + 0.2f;
                UpdateMapShader(backgroundMap);
                UpdateMapTiles(backgroundMap);
                UpdateMapScouting(backgroundMap);
                minimapFogOverlay.Invalidate();
                SynchronizeSailing();
            }
        }

        private void SynchronizeSailing()
        {
            var town = Community.PlayerCommunity;
            var world = GameManager.WorldManager?.World;
            if (town == null || world == null)
                return;

            var mapPosition = physics.Position;
            var rotation = physics.Rotation;
            if (sailDistance <= 0.001f && Quaternion.Angle(committedMapRotation, rotation) < 0.05f)
                return;

            // The map outline and the playable landmark use different shoreline coordinates.
            var position = mapPosition;
            var obstacle = ClosestObstacle(backgroundMap);
            if (obstacle != null && Vector2.Distance(mapPosition.Vector2TopDown(), obstacle.Position) <
                GameManager.Settings.GameplaySettings.SwimmingRadius)
            {
                var target = mapPosition.Vector2TopDown();
                var mapPoint = obstacle.MapLandmark.ReturnClosestMapPosition(mapPosition).Vector2TopDown();
                var worldPoint = obstacle.MapLandmark.ReturnClosestWorldPosition(mapPosition).Vector2TopDown();
                position = (worldPoint + (target - worldPoint).normalized *
                    Vector2.Distance(mapPoint, target)).Vector3TopDown();
            }

            town.StopNonInteractableProjects(position, GameManager.Settings.GameplaySettings.InteractionRadius);
            if (!world.RepositionTownheart(position, rotation, sailDistance))
            {
                ApplyCommittedMapPose();
                StopVelocity(physics, 0f, 0f);
                lastSailPosition = committedMapPosition;
                sailDistance = 0f;
                return;
            }

            committedMapPosition = mapPosition;
            committedMapRotation = rotation;
            sailDistance = 0f;
            PruneMapTiles(backgroundMap);
        }

        private void FinishSailing()
        {
            if (!sailing)
                return;
            sailing = false;
            if (backgroundMap == null || physics == null)
                return;

            var map = backgroundMap;
            var town = Community.PlayerCommunity;
            var world = GameManager.WorldManager?.World;
            try
            {
                if (town?.Engine == null || world == null)
                    return;
                MeasureSailing();
                SynchronizeSailing();
                ApplyCommittedMapPose();
            }
            finally
            {
                physics.StopMouseMovement();
                StopVelocity(physics, 0f, 0f);
                physics.enabled = false;
                map.AddMovementBlocker(map);
                map.Townheart.OnEndMove();
                MovementEvent.DispatchStoppedMoving();
            }
        }

        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.Close))]
        private static class IgnoreFullscreenCloseInBackground
        {
            private static bool Prefix(WorldMap __instance, ref bool __result)
            {
                if (instance == null || instance.backgroundMap != __instance)
                    return true;

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(FlotsamInputManager), nameof(FlotsamInputManager.PushCancelable))]
        private static class SkipBackgroundMapCancelHandlers
        {
            private static bool Prefix(ICancelable cancelable)
            {
                var map = instance?.backgroundMap;
                if (map == null || !(cancelable is CancelTriggerBase trigger) || trigger == null)
                    return true;

                // Map widgets enable with the preview, but only the fullscreen map owns Cancel.
                return !trigger.transform.IsChildOf(map.transform);
            }
        }

        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.Open))]
        private static class OpenFullscreenMap
        {
            private static void Prefix(WorldMap __instance)
            {
                if (instance != null && instance.backgroundMap == __instance)
                    instance.ReleaseBackgroundMap();
            }
        }

    }

    internal sealed class MinimapInput : MonoBehaviour, IScrollHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler, IDragHandler
    {
        internal Action<float> Zoom;
        internal Action<Vector2> Pan;
        internal bool Held { get; private set; }
        private bool panning;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                panning = true;
                Held = false;
            }
            else if (eventData.button == PointerEventData.InputButton.Left && !panning)
                Held = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Held = false;
            else if (eventData.button == PointerEventData.InputButton.Right)
                panning = false;
        }

        public void OnPointerExit(PointerEventData eventData) => Held = false;

        private void OnDisable()
        {
            Held = false;
            panning = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!panning || eventData.button != PointerEventData.InputButton.Right)
                return;

            var rect = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, null, out var current) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position - eventData.delta, null, out var previous))
                Pan?.Invoke(current - previous);
        }

        public void OnScroll(PointerEventData eventData) => Zoom?.Invoke(eventData.scrollDelta.y);
    }
}
