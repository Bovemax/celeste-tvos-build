using System;
using System.IO;
using Foundation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace LocalGameHost;

// Original synthetic probe; contains no game assets.
internal sealed class FnaProbe : Game
{
    private readonly GraphicsDeviceManager graphics;
    private SoundEffect? tone;
    private bool previousA;
    private bool connected;
    private bool pressed;
    private int external = -2, fmod, audio;
    private SpriteBatch? batch;
    private Texture2D? pixel;

    public FnaProbe()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.PreferredBackBufferWidth = 1280;
        graphics.PreferredBackBufferHeight = 720;
        graphics.IsFullScreen = true;
    }

    protected override void LoadContent()
    {
        var probe = Path.Combine(NSBundle.MainBundle.BundlePath, "ExternalProbe.dll");
        if (File.Exists(probe))
        {
            try { external = (int)(Program.InvokePayload("ExternalProbe.dll") ?? -1); }
            catch (Exception e) { external = -1; Console.Error.WriteLine("External assembly: " + e); }
        }
        Console.WriteLine("External assembly result: " + external);
        fmod = FmodProbe.Run();
        Console.WriteLine("FMOD probe result: " + fmod);
        batch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        // 120 ms quiet synthesized tone to exercise FAudio, not FMOD.
        var pcm = new byte[44100 * 12 / 100 * 2];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            short s = (short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 2500);
            pcm[i * 2] = (byte)s;
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }
        try { tone = new SoundEffect(pcm, 44100, AudioChannels.Mono); audio = 1; }
        catch (Exception e) { Console.Error.WriteLine("FAudio: " + e); }
        var path = Path.Combine(NSBundle.MainBundle.BundlePath, "Content", "probe.txt");
        Console.WriteLine("Content fixture present: " + File.Exists(path));
        base.LoadContent();
    }

    protected override void Update(GameTime time)
    {
        var state = GamePad.GetState(PlayerIndex.One);
        if (connected != state.IsConnected)
            Console.WriteLine("FNA gamepad connected: " + state.IsConnected);
        connected = state.IsConnected;
        pressed = state.Buttons.A == ButtonState.Pressed;
        if (pressed && !previousA)
        {
            Console.WriteLine("FNA A button pressed");
            try { if (tone != null) audio = tone.Play() ? 2 : -1; }
            catch (Exception e) { audio = -1; Console.Error.WriteLine("FAudio playback: " + e); }
        }
        previousA = pressed;
        base.Update(time);
    }

    protected override void Draw(GameTime time)
    {
        GraphicsDevice.Clear(pressed ? Color.Gold : connected ? Color.Green : Color.CornflowerBlue);
        if (batch != null && pixel != null)
        {
            // Six ordered status bars: external entry, FNA, SDL, content, write, FMOD; seventh FAudio.
            Color ExternalBit(int bit) => external == -2 ? Color.Gray :
                external >= 0 && (external & bit) != 0 ? Color.LimeGreen : Color.Red;
            var colors = new[] {
                external == -2 ? Color.Gray : external >= 0 ? Color.LimeGreen : Color.Red,
                ExternalBit(1), ExternalBit(2), ExternalBit(4), ExternalBit(8),
                fmod == 2 ? Color.LimeGreen : fmod == 1 ? Color.Gold : fmod == 0 ? Color.Gray : Color.Red,
                audio == 2 ? Color.LimeGreen : audio == 1 ? Color.Gold : Color.Red
            };
            var w = GraphicsDevice.Viewport.Width;
            var h = GraphicsDevice.Viewport.Height;
            batch.Begin();
            for (int i = 0; i < colors.Length; i++)
                batch.Draw(pixel, new Rectangle(w / 8, h / 8 + i * h / 10, w * 3 / 4, h / 14), colors[i]);
            batch.End();
        }
        base.Draw(time);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tone?.Dispose(); batch?.Dispose(); pixel?.Dispose(); }
        base.Dispose(disposing);
    }
}
