using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Pb.Game.Core;

namespace Pb.Game.Audio;

/// <summary>
/// The generated voices (presentation.jsonc "audio" → "cast" and "refereeVoice"): each voice speaks every line of the
/// script, one file per line, <c>res://art/voices/&lt;voice&gt;_&lt;slug&gt;.ogg</c>, found by the line's words
/// (<see cref="Slug"/>), so the script can be reordered or added to without renaming anything. Bots get a voice of
/// their character model. Voices are art: a missing file (or <c>--no-art</c>) leaves the line a subtitle only.
/// </summary>
public sealed class VoiceBank
{
    public const string Folder = "res://art/voices";

    private readonly Dictionary<int, string[]> _byModel = new();
    private readonly Dictionary<(string Voice, string Line), AudioStream?> _clips = new();
    private readonly int _models;

    public VoiceBank(AudioDef audio, int models)
    {
        _models = System.Math.Max(1, models);
        Referee = audio.RefereeVoice;
        foreach (IGrouping<int, CastVoiceDef> group in audio.Cast.GroupBy(c => c.Model))
        {
            _byModel[group.Key] = group.Select(c => c.Id).ToArray();
        }
    }

    public string Referee { get; }

    /// <summary>How many lines have been asked for that have no file.</summary>
    public int Missing { get; private set; }

    /// <summary>
    /// A line's file name: its words in lower case joined by dashes, apostrophes dropped ("They've got the case!" →
    /// "theyve-got-the-case").
    /// </summary>
    public static string Slug(string line)
    {
        var slug = new StringBuilder(line.Length);
        bool dash = false;
        foreach (char c in line.ToLowerInvariant())
        {
            if (c is '\'' or '’')
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (dash && slug.Length > 0)
                {
                    slug.Append('-');
                }

                slug.Append(c);
                dash = false;
            }
            else
            {
                dash = true;
            }
        }

        return slug.ToString();
    }

    /// <summary>A line's file: named for its voice and its words, so the art packs know whose it is (tools/package/art-packs.sh).</summary>
    public static string PathOf(string voice, string line) => $"{Folder}/{voice}_{Slug(line)}.ogg";

    /// <summary>
    /// The voice of the bot dealt look <paramref name="look"/> (models are dealt in turn, as <see cref="Player.CharacterVisual"/>
    /// does): one of its model's voices, the next for each further copy of the model; null if the model has none.
    /// </summary>
    public string? VoiceFor(int look)
    {
        int model = look % _models;
        return _byModel.TryGetValue(model, out string[]? voices) && voices.Length > 0 ? voices[look / _models % voices.Length] : null;
    }

    /// <summary>The recording of <paramref name="line"/> in <paramref name="voice"/>, or null.</summary>
    public AudioStream? Clip(string? voice, string line)
    {
        if (voice is null)
        {
            return null;
        }

        if (!_clips.TryGetValue((voice, line), out AudioStream? clip))
        {
            clip = ArtFiles.Load<AudioStream>(PathOf(voice, line));
            _clips[(voice, line)] = clip;
        }

        if (clip is null)
        {
            Missing++;
        }

        return clip;
    }
}
