using System.Collections.Generic;
using System.Threading.Tasks;
using DmytroUdovychenko.Tracking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DmytroUdovychenko.Tracking.HostDemo
{
    /// <summary>
    /// Drop-in demo for the host project. Auto-spawns on Play and builds a Canvas UI for live SDK testing.
    /// </summary>
    public sealed class TrackingSdkDemo : MonoBehaviour
    {
        private const string DEMO_ROOT_NAME = "Tracking SDK Demo";
        private const string DEMO_USER_ID = "demo-user-001";
        private const int PANEL_PADDING = 24;
        private const int SECTION_PADDING = 14;
        private const int BUTTON_HEIGHT = 44;

        private static readonly Color BackgroundColor = new Color32(17, 24, 39, 245);
        private static readonly Color PanelColor = new Color32(31, 41, 55, 250);
        private static readonly Color SectionColor = new Color32(45, 55, 72, 255);
        private static readonly Color TextColor = new Color32(243, 244, 246, 255);
        private static readonly Color MutedTextColor = new Color32(209, 213, 219, 255);
        private static readonly Color LocalColor = new Color32(16, 185, 129, 255);
        private static readonly Color StagingColor = new Color32(245, 158, 11, 255);
        private static readonly Color ProductionColor = new Color32(59, 130, 246, 255);
        private static readonly Color ErrorColor = new Color32(239, 68, 68, 255);
        private static readonly Color NeutralColor = new Color32(75, 85, 99, 255);

        private int m_counter;
        private string m_currentTarget = "Not initialized";
        private string m_currentEndpoint = "-";
        private string m_lastAction = "Choose an initialization mode.";
        private string m_lastMapResult = "None";
        private Font m_font;
        private Text m_targetText;
        private Text m_statusText;
        private Text m_metricsText;
        private Text m_enabledButtonLabel;
        private Text m_privacyButtonLabel;

        private void Awake()
        {
            EnsureEventSystem();
            BuildCanvas();
        }

        private void Update()
        {
            RefreshStatus();
        }

        private void InitializeLocal()
        {
            Tracker.Dispose();
            Tracker.Init(DEMO_USER_ID, minLogLevel: TrackingLogLevel.Debug);
            m_currentTarget = "Fake server / simulated";
            m_currentEndpoint = TrackingConfig.DEFAULT_ENDPOINT;
            m_lastAction = Tracker.IsInitialized
                ? "Initialized locally. No network is used; delivery is simulated."
                : "Local initialization failed.";
        }

        private void InitializeFakeChaos()
        {
            Tracker.Dispose();
            Tracker.Init(DEMO_USER_ID, ServerEnvironment.FakeServerChaos, minLogLevel: TrackingLogLevel.Debug);
            m_currentTarget = "Fake server / simulated chaos";
            m_currentEndpoint = TrackingConfig.DEFAULT_ENDPOINT;
            m_lastAction = Tracker.IsInitialized
                ? "Initialized against the simulated chaos fake (~20% transient failures, no network). Use send buttons to watch retries / dead-letter."
                : "Fake chaos initialization failed.";
        }

        private void InitializeHttpTestChaos()
        {
            Tracker.Dispose();
            Tracker.Init(DEMO_USER_ID, ServerEnvironment.HttpTestServerChaos, minLogLevel: TrackingLogLevel.Debug);
            m_currentTarget = "Live test receiver (stub) / chaos HTTP";
            m_currentEndpoint = TrackingConfig.HTTP_TEST_CHAOS_ENDPOINT;
            m_lastAction = Tracker.IsInitialized
                ? "Initialized against the live test receiver (stub) in chaos mode (~20% transient 503s). Use send buttons to watch retries."
                : "Live test receiver (stub) initialization was blocked. Check connectivity and Console logs.";
        }

        private void InitializeHttpTest()
        {
            Tracker.Dispose();
            Tracker.Init(DEMO_USER_ID, ServerEnvironment.HttpTestServer, minLogLevel: TrackingLogLevel.Debug);
            m_currentTarget = "Live test receiver (stub) / clean HTTP";
            m_currentEndpoint = TrackingConfig.HTTP_TEST_ENDPOINT;
            m_lastAction = Tracker.IsInitialized
                ? "Initialized against the live test receiver (stub). Valid events should deliver without transport errors."
                : "Live test receiver (stub) initialization was blocked. Check connectivity and Console logs.";
        }

        private void SendValidMessage()
        {
            int sequence = NextSequence();
            bool accepted = Tracker.SendMessage("demo_message_" + sequence);
            m_lastAction = accepted
                ? "SendMessage valid accepted: demo_message_" + sequence
                : "SendMessage valid was rejected. Initialize tracking first.";

            if (accepted)
            {
                _ = FlushMessageAsync("SendMessage valid");
            }
        }

        private void SendInvalidMessage()
        {
            bool accepted = Tracker.SendMessage(" ");
            m_lastAction = accepted
                ? "Unexpected: invalid SendMessage was accepted."
                : "SendMessage invalid rejected. Expected Error log: empty message.";
        }

        private async void SendValidMap()
        {
            int sequence = NextSequence();
            Dictionary<string, object> map = new Dictionary<string, object>
            {
                ["event"] = "purchase",
                ["sequence"] = sequence,
                ["sku"] = "coins_500",
                ["price"] = 4.99,
            };

            await SendMapAsync("SendMap valid", map);
        }

        private async void SendMixedMap()
        {
            int sequence = NextSequence();
            Dictionary<string, object> map = new Dictionary<string, object>
            {
                ["event"] = "tutorial_step",
                ["sequence"] = sequence,
                [""] = "dropped_empty_key",
                ["null_value"] = null,
            };

            await SendMapAsync("SendMap mixed", map);
        }

        private async void SendEmptyMap()
        {
            await SendMapAsync("SendMap empty", new Dictionary<string, object>());
        }

        private async void SendInvalidMap()
        {
            Dictionary<string, object> map = new Dictionary<string, object>
            {
                [""] = "invalid_empty_key",
                ["null_value"] = null,
            };

            await SendMapAsync("SendMap invalid entries", map);
        }

        private async void FlushNow()
        {
            m_lastAction = "Flush pending...";
            await Tracker.FlushAsync();
            m_lastAction = "Flush completed.";
        }

        private void ToggleEnabled()
        {
            bool nextEnabled = !Tracker.IsEnabled;
            Tracker.SetEnabled(nextEnabled);
            m_lastAction = nextEnabled
                ? "Tracking enabled."
                : "Tracking disabled. Buffered and persisted data were purged.";
        }

        private void TogglePrivacyMode()
        {
            bool nextPrivacyMode = !Tracker.IsPrivacyMode;
            Tracker.SetPrivacyMode(nextPrivacyMode);
            m_lastAction = nextPrivacyMode
                ? "Privacy mode enabled. New events use userId anonymous."
                : "Privacy mode disabled. New events use the configured userId.";
        }

        private void Purge()
        {
            Tracker.Purge();
            m_lastAction = "Purge completed. Buffered, persisted, and dead-lettered events were cleared.";
        }

        private async Task FlushMessageAsync(string label)
        {
            await Tracker.FlushAsync();
            if (this == null)
            {
                return;
            }
            m_lastAction = label + " flushed.";
        }

        private async Task SendMapAsync(string label, Dictionary<string, object> map)
        {
            m_lastMapResult = label + ": pending";
            Task<bool> delivery = Tracker.SendMapAsync(map);
            await Tracker.FlushAsync();
            bool delivered = await delivery;
            if (this == null)
            {
                return;
            }

            m_lastMapResult = label + ": " + (delivered ? "delivered" : "failed / rejected");
            m_lastAction = delivered
                ? label + " delivered."
                : label + " returned false. Check Console logs for validation or transport errors.";
        }

        private int NextSequence()
        {
            m_counter++;
            return m_counter;
        }

        private void RefreshStatus()
        {
            if (m_targetText == null || m_statusText == null || m_metricsText == null)
            {
                return;
            }

            TrackingMetricsSnapshot metrics = Tracker.Metrics;
            m_targetText.text =
                "Target: " + m_currentTarget + "\n" +
                "Endpoint: " + m_currentEndpoint + "\n" +
                "User: " + (Tracker.UserId ?? "-") + "\n" +
                "Session: " + (Tracker.SessionId ?? "-");

            m_statusText.text =
                "Last action: " + m_lastAction + "\n" +
                "Last map: " + m_lastMapResult;

            m_metricsText.text =
                "Initialized: " + Tracker.IsInitialized + "\n" +
                "Enabled: " + Tracker.IsEnabled + "\n" +
                "Privacy mode: " + Tracker.IsPrivacyMode + "\n" +
                "Enqueued: " + metrics.Enqueued + "\n" +
                "Sent: " + metrics.Sent + "\n" +
                "Dropped: " + metrics.Dropped + "\n" +
                "Retried: " + metrics.Retried + "\n" +
                "Given up: " + metrics.GivenUp + "\n" +
                "Dead-lettered: " + metrics.DeadLettered + " (queued: " + (Tracker.DeadLetter?.Count ?? 0) + ")";

            if (m_enabledButtonLabel != null)
            {
                m_enabledButtonLabel.text = Tracker.IsEnabled ? "Disable + purge" : "Enable tracking";
            }
            if (m_privacyButtonLabel != null)
            {
                m_privacyButtonLabel.text = Tracker.IsPrivacyMode ? "Privacy: ON" : "Privacy: OFF";
            }
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildCanvas()
        {
            Font font = LoadFont();
            GameObject canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject backdropObject = CreateUiObject("Backdrop", canvasObject.transform);
            Image backdrop = backdropObject.AddComponent<Image>();
            backdrop.color = BackgroundColor;
            Stretch(backdropObject.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            GameObject panelObject = CreateUiObject("Panel", backdropObject.transform);
            Image panel = panelObject.AddComponent<Image>();
            panel.color = PanelColor;
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            Stretch(panelRect, 0f, 0f, 0f, 0f);

            VerticalLayoutGroup panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(PANEL_PADDING, PANEL_PADDING, PANEL_PADDING, PANEL_PADDING);
            panelLayout.spacing = 12f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            Text title = CreateText(panelObject.transform, "Title", "Dmytro Udovychenko Tracking SDK Demo", 30, FontStyle.Bold, TextColor);
            title.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(title.gameObject, 38f);

            Text subtitle = CreateText(
                panelObject.transform,
                "Subtitle",
                "Initialize a target, then send valid and intentionally invalid events to watch validation, retries, and metrics live. The HTTP buttons hit a developer live test receiver (stub) that validates, logs, and returns 200 — not a real backend.",
                15,
                FontStyle.Normal,
                MutedTextColor);
            subtitle.alignment = TextAnchor.MiddleLeft;
            SetPreferredHeight(subtitle.gameObject, 32f);

            Transform initSection = CreateSection(panelObject.transform, "Initialization", font);
            Transform fakeRow = CreateRow(initSection);
            CreateButton(fakeRow, "Fake (clean)", LocalColor, InitializeLocal);
            CreateButton(fakeRow, "Fake (chaos)", StagingColor, InitializeFakeChaos);
            Transform httpRow = CreateRow(initSection);
            CreateButton(httpRow, "Test stub (clean)", ProductionColor, InitializeHttpTest);
            CreateButton(httpRow, "Test stub (chaos)", StagingColor, InitializeHttpTestChaos);

            Transform sendSection = CreateSection(panelObject.transform, "Send events", font);
            Transform messageRow = CreateRow(sendSection);
            CreateButton(messageRow, "Send message", LocalColor, SendValidMessage);
            CreateButton(messageRow, "Message error", ErrorColor, SendInvalidMessage);
            CreateButton(messageRow, "Flush now", NeutralColor, FlushNow);

            Transform mapRow = CreateRow(sendSection);
            CreateButton(mapRow, "Map valid", LocalColor, SendValidMap);
            CreateButton(mapRow, "Map mixed warnings", StagingColor, SendMixedMap);
            CreateButton(mapRow, "Map empty error", ErrorColor, SendEmptyMap);
            CreateButton(mapRow, "Map invalid error", ErrorColor, SendInvalidMap);

            Transform controlsSection = CreateSection(panelObject.transform, "Runtime controls", font);
            Transform controlsRow = CreateRow(controlsSection);
            Button enabledButton = CreateButton(controlsRow, "Disable + purge", NeutralColor, ToggleEnabled);
            m_enabledButtonLabel = enabledButton.GetComponentInChildren<Text>();
            Button privacyButton = CreateButton(controlsRow, "Privacy: OFF", NeutralColor, TogglePrivacyMode);
            m_privacyButtonLabel = privacyButton.GetComponentInChildren<Text>();
            CreateButton(controlsRow, "Purge", ErrorColor, Purge);

            Transform statusSection = CreateSection(panelObject.transform, "Live status", font);
            Transform statusRow = CreateRow(statusSection);
            m_targetText = CreateText(statusRow, "TargetText", string.Empty, 14, FontStyle.Normal, MutedTextColor);
            m_statusText = CreateText(statusRow, "StatusText", string.Empty, 14, FontStyle.Normal, MutedTextColor);
            m_metricsText = CreateText(statusRow, "MetricsText", string.Empty, 14, FontStyle.Normal, MutedTextColor);
            SetFlexibleTextColumn(m_targetText.gameObject);
            SetFlexibleTextColumn(m_statusText.gameObject);
            SetFlexibleTextColumn(m_metricsText.gameObject);

            m_font = font;
        }

        private Transform CreateSection(Transform parent, string title, Font font)
        {
            GameObject sectionObject = CreateUiObject(title + " Section", parent);
            Image section = sectionObject.AddComponent<Image>();
            section.color = SectionColor;

            VerticalLayoutGroup layout = sectionObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(SECTION_PADDING, SECTION_PADDING, SECTION_PADDING, SECTION_PADDING);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = sectionObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text heading = CreateText(sectionObject.transform, title, title, 17, FontStyle.Bold, TextColor);
            heading.font = font;
            SetPreferredHeight(heading.gameObject, 24f);

            return sectionObject.transform;
        }

        private Transform CreateRow(Transform parent)
        {
            GameObject rowObject = CreateUiObject("Row", parent);
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = rowObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rowObject.transform;
        }

        private Button CreateButton(Transform parent, string label, Color color, UnityAction action)
        {
            GameObject buttonObject = CreateUiObject(label + " Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = Color.white;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = BUTTON_HEIGHT;
            layoutElement.preferredHeight = BUTTON_HEIGHT;
            layoutElement.flexibleWidth = 1f;

            Text text = CreateText(buttonObject.transform, "Label", label, 14, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.GetComponent<RectTransform>(), 10f, 4f, 10f, 4f);
            return button;
        }

        private Text CreateText(Transform parent, string name, string value, int size, FontStyle style, Color color)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = textObject.AddComponent<Text>();
            text.font = m_font ?? LoadFont();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Font LoadFont()
        {
            if (m_font != null)
            {
                return m_font;
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            m_font = font;
            return m_font;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetPreferredHeight(GameObject gameObject, float height)
        {
            LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = height;
            layoutElement.minHeight = height;
        }

        private static void SetFlexibleTextColumn(GameObject gameObject)
        {
            LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = 132f;
        }
    }
}
