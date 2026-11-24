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

    public FnaProbe()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.PreferredBackBufferWidth = 1280;
        graphics.PreferredBackBufferHeight = 720;
        graphics.IsFullScreen = true;
    }

    protected override void LoadContent()
    {
        // 120 ms quiet synthesized tone to exercise FAudio, not FMOD.
        var pcm = new byte[44100 * 12 / 100 * 2];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            short s = (short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 2500);
            pcm[i * 2] = (byte)s;
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }
        try { tone = new SoundEffect(pcm, 44100, AudioChannels.Mono); }
        catch (Exception e) { Console.Error.WriteLine("FAudio: " + e); }
        var path = Path.Combine(NSBundle.MainBundle.BundlePath, "GameAssemblies", "Content", "probe.txt");
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
            try { tone?.Play(); }
            catch (Exception e) { Console.Error.WriteLine("FAudio playback: " + e); }
        }
        previousA = pressed;
        base.Update(time);
    }

    protected override void Draw(GameTime time)
    {
        GraphicsDevice.Clear(pressed ? Color.Gold : connected ? Color.Green : Color.CornflowerBlue);
        base.Draw(time);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tone?.Dispose();
        base.Dispose(disposing);
    }
}
