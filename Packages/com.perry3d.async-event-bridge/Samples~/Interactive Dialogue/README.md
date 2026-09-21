# Interactive Dialogue + Live Code

A playable, zero-asset AsyncEventBridge showcase for Unity.

## Run it

1. In Package Manager, select **AsyncEventBridge**.
2. Import **Interactive Dialogue + Live Code** from the Samples section.
3. Open `Scenes/AsyncEventBridge Dialogue Demo.unity`.
4. Enter Play Mode.
5. Choose **Yes** or **No** and watch the live code panel.

The scene is intentionally render-pipeline independent and creates its presentation at runtime, so it does not require uGUI, TextMesh Pro, sprites, fonts, materials, or other sample dependencies.

## What it demonstrates

The dialogue uses a real `UnityEvent<bool>` as the player's answer event:

```csharp
Ask("Will you enter the old ruins?");

var answer = await choiceSelected.WaitAsync(this);

if (answer)
    Say("Then let's go.");
else
    Say("A wise choice.");

await continueRequested.WaitAsync(this);

onConversationFinished.Invoke(answer);
```

While the game is waiting for the player's answer, the live code panel highlights:

```csharp
await choiceSelected.WaitAsync(this);
```

Clicking **Yes** or **No** invokes the `UnityEvent<bool>`. AsyncEventBridge removes its temporary listener, completes the Unity `Awaitable<bool>`, and execution continues through the matching branch.

The `this` owner overload ties each wait to the `MonoBehaviour` lifecycle, so destroying the demo object cancels the outstanding wait automatically.

## Why this sample is useful for screenshots and video

The scene was designed to communicate the package visually:

- the character and choice UI show the gameplay outcome;
- the live code panel shows exactly where execution is suspended;
- the event-flow strip shows `UnityEvent -> WaitAsync -> Awaitable resumes`;
- the status badge changes between waiting, resumed, publishing, and complete states;
- the same scene can be replayed repeatedly for screen capture.

For a store page or short video, a strong sequence is:

1. capture the scene while `WaitAsync(this)` is highlighted;
2. click **Yes**;
3. capture the event-flow strip as the Awaitable resumes;
4. show the NPC response and the next awaited event;
5. end on the completed state with the short async method still visible.

## Source

`Scripts/InteractiveDialogueDemo.cs` contains the complete sample. The UI is intentionally code-driven so the sample remains portable and easy to inspect.
