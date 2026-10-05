using Godot;

namespace Pb.Game.Audio;

/// <summary>
/// The bank's sounds as Godot streams, each made the first time it's played and all freed with their owner (a scene's
/// <see cref="AudioDirector"/>, the menus), so nothing outlives the scene that used it.
/// </summary>
public sealed class SoundSet
{
    private readonly AudioStreamWav[]?[] _streams;

    public SoundSet(SoundBank bank)
    {
        Bank = bank;
        _streams = new AudioStreamWav[bank.SoundCount][];
    }

    public SoundBank Bank { get; }

    /// <summary>Every variation of a sound.</summary>
    public AudioStreamWav[] Streams(Sfx sfx)
    {
        int index = (int)sfx;
        if (_streams[index] is { } made)
        {
            return made;
        }

        var streams = new AudioStreamWav[Bank.VariationsOf(sfx)];
        for (int v = 0; v < streams.Length; v++)
        {
            streams[v] = Synth.Stream(Bank.Pcm(sfx, v), Bank.RateOf(sfx), Bank.Loops(sfx));
        }

        _streams[index] = streams;
        return streams;
    }

    /// <summary>One variation of a sound, picked by <paramref name="pick"/> (wrapped).</summary>
    public AudioStreamWav Get(Sfx sfx, int pick)
    {
        AudioStreamWav[] streams = Streams(sfx);
        return streams[(int)((uint)pick % (uint)streams.Length)];
    }

    /// <summary>Frees every stream made so far.</summary>
    public void Release()
    {
        for (int i = 0; i < _streams.Length; i++)
        {
            if (_streams[i] is { } streams)
            {
                foreach (AudioStreamWav stream in streams)
                {
                    stream.Dispose();
                }

                _streams[i] = null;
            }
        }
    }
}
