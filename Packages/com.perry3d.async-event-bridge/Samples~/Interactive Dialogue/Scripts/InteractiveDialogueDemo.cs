using System;
using System.Threading;
using AsyncEventBridge.Unity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AsyncEventBridge.Unity.Samples
{
    /// <summary>
    /// Self-contained marketing/demo scene that turns a real UnityEvent wait into a visible gameplay flow.
    /// The matching scene is intentionally asset-free so it imports cleanly into any render pipeline.
    /// </summary>
    public sealed class InteractiveDialogueDemo : MonoBehaviour
    {
        private const string SceneName = "AsyncEventBridge Dialogue Demo";

        private static readonly string[] CodeLines =
        {
            "<color=#8BE9FD>private async Awaitable</color> RunConversationAsync()",
            "{",
            "    Ask(<color=#F1FA8C>\"Will you enter the old ruins?\"</color>);",
            "",
            "    <color=#FF79C6>var</color> answer =",
            "        <color=#50FA7B>await</color> choiceSelected.<color=#8BE9FD>WaitAsync</color>(this);",
            "",
            "    <color=#FF79C6>if</color> (answer)",
            "        Say(<color=#F1FA8C>\"Then let's go.\"</color>);",
            "    <color=#FF79C6>else</color>",
            "        Say(<color=#F1FA8C>\"A wise choice.\"</color>);",
            "",
            "    <color=#50FA7B>await</color> continueRequested.<color=#8BE9FD>WaitAsync</color>(this);",
            "",
            "    onConversationFinished.<color=#8BE9FD>Invoke</color>(answer);",
            "}",
        };

        [SerializeField]
        private UnityEvent<bool> onConversationFinished = new UnityEvent<bool>();

        private readonly UnityEvent<bool> _choiceSelected = new UnityEvent<bool>();
        private readonly UnityEvent _continueRequested = new UnityEvent();
        private readonly UnityEvent _restartRequested = new UnityEvent();

        private DemoPhase _phase;
        private int _activeCodeLine = -1;
        private string _dialogue = string.Empty;
        private string _status = "STARTING";
        private float _eventFlashUntil;

        private Texture2D _portrait;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _speakerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _codeStyle;
        private GUIStyle _lineNumberStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _statusStyle;
        private bool _stylesReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != SceneName)
            {
                return;
            }

            if (FindObjectOfType<InteractiveDialogueDemo>() != null)
            {
                return;
            }

            var host = new GameObject("AsyncEventBridge - Interactive Dialogue Demo");
            host.AddComponent<InteractiveDialogueDemo>();
        }

        private void Awake()
        {
            _portrait = CreatePortraitTexture(160);
        }

        public async Awaitable Start()
        {
            var cancellationToken = destroyCancellationToken;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await RunConversationAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Awaitable RunConversationAsync(CancellationToken cancellationToken)
        {
            _phase = DemoPhase.Asking;
            _activeCodeLine = 2;
            _status = "RUNNING ASYNC GAMEPLAY";
            _dialogue = "Traveler, the old ruins are open again. Will you enter?";
            await HoldFramesAsync(26, cancellationToken);

            _phase = DemoPhase.WaitingForChoice;
            _activeCodeLine = 5;
            _status = "WAITING ON UNITYEVENT";
            var answer = await _choiceSelected.WaitAsync(this);

            _phase = DemoPhase.Branching;
            _activeCodeLine = 7;
            _status = "AWAITABLE RESUMED";
            await HoldFramesAsync(16, cancellationToken);

            _activeCodeLine = answer ? 8 : 10;
            _dialogue = answer
                ? "Then let's go. Stay close — the bridge ahead is unstable."
                : "A wise choice. Some doors can wait until we're ready.";
            await HoldFramesAsync(46, cancellationToken);

            _phase = DemoPhase.WaitingForContinue;
            _activeCodeLine = 12;
            _status = "WAITING ON NEXT EVENT";
            await _continueRequested.WaitAsync(this);

            _phase = DemoPhase.Publishing;
            _activeCodeLine = 14;
            _status = "PUBLISHING UNITYEVENT";
            onConversationFinished.Invoke(answer);
            await HoldFramesAsync(30, cancellationToken);

            _phase = DemoPhase.Completed;
            _activeCodeLine = -1;
            _status = "FLOW COMPLETE";
            _dialogue = answer
                ? "That whole interaction stayed inside one readable async method."
                : "The No branch used the same await, cleanup, and lifecycle-safe flow.";

            await _restartRequested.WaitAsync(this);
        }

        private static async Awaitable HoldFramesAsync(int frameCount, CancellationToken cancellationToken)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            var scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            scale = Mathf.Max(scale, 0.35f);

            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;
            var height = Screen.height / scale;

            DrawBackground(width, height);
            DrawHeader(width);
            DrawDialoguePanel(width, height);
            DrawCodePanel(width, height);

            GUI.matrix = previousMatrix;
        }

        private void DrawBackground(float width, float height)
        {
            FillRect(new Rect(0f, 0f, width, height), new Color32(11, 16, 29, 255));
            FillRect(new Rect(0f, 0f, width, 88f), new Color32(15, 22, 39, 255));
            FillRect(new Rect(0f, 87f, width, 1f), new Color32(46, 58, 82, 255));
        }

        private void DrawHeader(float width)
        {
            GUI.Label(new Rect(38f, 19f, 760f, 36f), "AsyncEventBridge", _titleStyle);
            GUI.Label(
                new Rect(40f, 53f, 820f, 24f),
                "Interactive Dialogue  •  UnityEvent → Awaitable → readable gameplay",
                _subtitleStyle);

            var badge = new Rect(width - 250f, 24f, 205f, 40f);
            FillRect(badge, new Color32(31, 44, 68, 255));
            GUI.Label(badge, "LIVE PLAY MODE", _statusStyle);
        }

        private void DrawDialoguePanel(float width, float height)
        {
            const float margin = 38f;
            var panelWidth = Mathf.Min(565f, width * 0.42f);
            var panel = new Rect(margin, 118f, panelWidth, height - 156f);

            FillRect(panel, new Color32(18, 26, 44, 255));
            StrokeRect(panel, new Color32(45, 60, 87, 255), 1f);

            GUI.Label(
                new Rect(panel.x + 26f, panel.y + 22f, panel.width - 52f, 28f),
                "INTERACTIVE GAMEPLAY",
                _subtitleStyle);

            var portraitRect = new Rect(panel.x + 28f, panel.y + 72f, 146f, 146f);
            FillRect(
                new Rect(portraitRect.x - 4f, portraitRect.y - 4f, portraitRect.width + 8f, portraitRect.height + 8f),
                new Color32(67, 91, 128, 255));
            GUI.DrawTexture(portraitRect, _portrait, ScaleMode.ScaleToFit, true);

            GUI.Label(
                new Rect(panel.x + 198f, panel.y + 91f, panel.width - 226f, 30f),
                "MIRA",
                _speakerStyle);
            GUI.Label(
                new Rect(panel.x + 198f, panel.y + 126f, panel.width - 226f, 68f),
                "Ruins guide\n<color=#8796B3>NPC / event producer</color>",
                _bodyStyle);

            var speechRect = new Rect(
                panel.x + 28f,
                panel.y + 248f,
                panel.width - 56f,
                Mathf.Max(150f, panel.height - 430f));
            FillRect(speechRect, new Color32(24, 34, 56, 255));
            StrokeRect(speechRect, new Color32(54, 71, 103, 255), 1f);

            GUI.Label(
                new Rect(speechRect.x + 24f, speechRect.y + 20f, speechRect.width - 48f, speechRect.height - 40f),
                _dialogue,
                _bodyStyle);

            var actionY = panel.yMax - 145f;
            if (_phase == DemoPhase.WaitingForChoice)
            {
                var buttonWidth = (panel.width - 72f) * 0.5f;
                if (DrawButton(
                        new Rect(panel.x + 28f, actionY, buttonWidth, 62f),
                        "YES — ENTER",
                        new Color32(47, 125, 105, 255)))
                {
                    FlashEvent();
                    _choiceSelected.Invoke(true);
                }

                if (DrawButton(
                        new Rect(panel.x + 44f + buttonWidth, actionY, buttonWidth, 62f),
                        "NO — STAY",
                        new Color32(84, 69, 99, 255)))
                {
                    FlashEvent();
                    _choiceSelected.Invoke(false);
                }
            }
            else if (_phase == DemoPhase.WaitingForContinue)
            {
                if (DrawButton(
                        new Rect(panel.x + 28f, actionY, panel.width - 56f, 62f),
                        "CONTINUE",
                        new Color32(48, 93, 148, 255)))
                {
                    FlashEvent();
                    _continueRequested.Invoke();
                }
            }
            else if (_phase == DemoPhase.Completed)
            {
                if (DrawButton(
                        new Rect(panel.x + 28f, actionY, panel.width - 56f, 62f),
                        "REPLAY THE FLOW",
                        new Color32(48, 93, 148, 255)))
                {
                    _restartRequested.Invoke();
                }
            }
            else
            {
                var waitingRect = new Rect(panel.x + 28f, actionY, panel.width - 56f, 62f);
                FillRect(waitingRect, new Color32(23, 32, 51, 255));
                GUI.Label(waitingRect, "The async method is running…", _statusStyle);
            }

            GUI.Label(
                new Rect(panel.x + 30f, panel.yMax - 62f, panel.width - 60f, 30f),
                "No coroutines. No manual AddListener / RemoveListener cleanup.",
                _smallStyle);
        }

        private void DrawCodePanel(float width, float height)
        {
            const float rightMargin = 38f;
            var x = Mathf.Min(640f, width * 0.46f);
            var panel = new Rect(x, 118f, width - x - rightMargin, height - 156f);

            FillRect(panel, new Color32(14, 21, 36, 255));
            StrokeRect(panel, new Color32(45, 60, 87, 255), 1f);

            GUI.Label(
                new Rect(panel.x + 26f, panel.y + 22f, 260f, 28f),
                "LIVE CODE",
                _subtitleStyle);

            var statusRect = new Rect(panel.xMax - 290f, panel.y + 17f, 260f, 38f);
            FillRect(
                statusRect,
                _phase == DemoPhase.WaitingForChoice || _phase == DemoPhase.WaitingForContinue
                    ? new Color32(74, 68, 36, 255)
                    : new Color32(30, 69, 65, 255));
            GUI.Label(statusRect, _status, _statusStyle);

            var codeRect = new Rect(panel.x + 24f, panel.y + 72f, panel.width - 48f, 535f);
            FillRect(codeRect, new Color32(9, 14, 25, 255));
            StrokeRect(codeRect, new Color32(39, 52, 77, 255), 1f);

            const float lineHeight = 30f;
            var startY = codeRect.y + 16f;

            for (var index = 0; index < CodeLines.Length; index++)
            {
                var lineRect = new Rect(codeRect.x + 8f, startY + index * lineHeight, codeRect.width - 16f, lineHeight);
                if (index == _activeCodeLine)
                {
                    FillRect(lineRect, new Color32(39, 63, 82, 255));
                    FillRect(new Rect(lineRect.x, lineRect.y, 4f, lineRect.height), new Color32(80, 250, 123, 255));
                }

                GUI.Label(
                    new Rect(lineRect.x + 10f, lineRect.y, 34f, lineRect.height),
                    (index + 1).ToString("00"),
                    _lineNumberStyle);
                GUI.Label(
                    new Rect(lineRect.x + 52f, lineRect.y, lineRect.width - 60f, lineRect.height),
                    CodeLines[index],
                    _codeStyle);
            }

            var traceY = codeRect.yMax + 28f;
            GUI.Label(
                new Rect(panel.x + 26f, traceY, panel.width - 52f, 24f),
                "WHAT IS HAPPENING RIGHT NOW",
                _subtitleStyle);

            traceY += 38f;
            DrawTraceStep(new Rect(panel.x + 26f, traceY, 190f, 56f), "UnityEvent<bool>", 0);
            DrawArrow(new Rect(panel.x + 220f, traceY, 42f, 56f));
            DrawTraceStep(new Rect(panel.x + 266f, traceY, 190f, 56f), "WaitAsync(this)", 1);
            DrawArrow(new Rect(panel.x + 460f, traceY, 42f, 56f));
            DrawTraceStep(
                new Rect(panel.x + 506f, traceY, Mathf.Max(150f, panel.width - 532f), 56f),
                "Awaitable resumes",
                2);

            GUI.Label(
                new Rect(panel.x + 28f, panel.yMax - 48f, panel.width - 56f, 24f),
                "The highlighted line is the real execution point — the demo is driven by AsyncEventBridge.",
                _smallStyle);
        }

        private void DrawTraceStep(Rect rect, string text, int step)
        {
            var activeStep = GetTraceStep();
            var isActive = step == activeStep;
            var flash = Time.unscaledTime < _eventFlashUntil && step == 0;

            FillRect(
                rect,
                flash
                    ? new Color32(55, 133, 105, 255)
                    : isActive
                        ? new Color32(40, 78, 102, 255)
                        : new Color32(24, 34, 54, 255));
            StrokeRect(
                rect,
                isActive || flash
                    ? new Color32(91, 184, 170, 255)
                    : new Color32(45, 60, 87, 255),
                1f);
            GUI.Label(rect, text, _statusStyle);
        }

        private void DrawArrow(Rect rect)
        {
            GUI.Label(rect, "→", _speakerStyle);
        }

        private int GetTraceStep()
        {
            if (Time.unscaledTime < _eventFlashUntil)
            {
                return 0;
            }

            if (_phase == DemoPhase.WaitingForChoice || _phase == DemoPhase.WaitingForContinue)
            {
                return 1;
            }

            if (_phase == DemoPhase.Branching || _phase == DemoPhase.Publishing || _phase == DemoPhase.Completed)
            {
                return 2;
            }

            return 0;
        }

        private void FlashEvent()
        {
            _eventFlashUntil = Time.unscaledTime + 0.45f;
            _status = "EVENT FIRED";
        }

        private bool DrawButton(Rect rect, string text, Color32 background)
        {
            FillRect(rect, background);
            StrokeRect(rect, new Color32(110, 137, 173, 255), 1f);
            return GUI.Button(rect, text, _buttonStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(239, 245, 255, 255) },
            };

            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(139, 161, 193, 255) },
            };

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color32(225, 233, 245, 255) },
            };

            _speakerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color32(232, 241, 255, 255) },
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal =
                {
                    background = null,
                    textColor = Color.white,
                },
                hover =
                {
                    background = null,
                    textColor = Color.white,
                },
                active =
                {
                    background = null,
                    textColor = Color.white,
                },
            };

            _codeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                richText = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color32(214, 222, 237, 255) },
            };

            _lineNumberStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color32(83, 99, 126, 255) },
            };

            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color32(120, 139, 168, 255) },
            };

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color32(233, 241, 252, 255) },
            };

            _stylesReady = true;
        }

        private static void FillRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void StrokeRect(Rect rect, Color color, float thickness)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Texture2D CreatePortraitTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AsyncEventBridge Demo Portrait",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[size * size];
            var center = (size - 1) * 0.5f;
            var radius = size * 0.48f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var color = new Color32(0, 0, 0, 0);

                    if (distance <= radius)
                    {
                        color = new Color32(31, 56, 79, 255);

                        var headX = dx / (size * 0.28f);
                        var headY = (dy + size * 0.02f) / (size * 0.33f);
                        if (headX * headX + headY * headY <= 1f)
                        {
                            color = new Color32(217, 173, 142, 255);
                        }

                        if (dy > size * 0.12f && Mathf.Abs(dx) < size * 0.30f)
                        {
                            color = new Color32(60, 43, 57, 255);
                        }

                        var leftEye = Mathf.Abs(dx + size * 0.09f) < size * 0.022f &&
                                      Mathf.Abs(dy + size * 0.04f) < size * 0.018f;
                        var rightEye = Mathf.Abs(dx - size * 0.09f) < size * 0.022f &&
                                       Mathf.Abs(dy + size * 0.04f) < size * 0.018f;
                        if (leftEye || rightEye)
                        {
                            color = new Color32(31, 35, 48, 255);
                        }

                        var mouth = Mathf.Abs(dy - size * 0.10f) < size * 0.012f &&
                                    Mathf.Abs(dx) < size * 0.08f;
                        if (mouth)
                        {
                            color = new Color32(116, 65, 68, 255);
                        }
                    }

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (_portrait != null)
            {
                Destroy(_portrait);
            }
        }

        private enum DemoPhase
        {
            Asking,
            WaitingForChoice,
            Branching,
            WaitingForContinue,
            Publishing,
            Completed,
        }
    }
}
