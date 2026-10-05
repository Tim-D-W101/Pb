using System;
using System.Collections.Generic;
using System.Linq;

namespace Pb.Game.Tools;

/// <summary>
/// Cuts one generated take of a voice's whole script into its lines (<c>tools/art/import.sh voice</c>): it measures the
/// loudness every 10 ms, finds the quiet stretches between the speech, and cuts at the middle of as many of them as there
/// are line breaks, chosen for being long and for giving each line about the length its words take to say (so a comma's
/// pause inside a line isn't cut even when it's as long as the pause between two lines). Each line is then trimmed of
/// its silence, with a little kept either side.
/// </summary>
public static class VoiceSplitter
{
    private const float Frame = 0.01f;

    /// <summary>Kept before a line's first sound and after its last (s).</summary>
    private const float Lead = 0.04f, Tail = 0.09f;

    /// <summary>A line: its first and last sample, and the pause cut at before it (s).</summary>
    public readonly record struct Line(int Start, int End, float PauseBefore);

    /// <summary>
    /// Splits <paramref name="samples"/> into the lines of <paramref name="script"/>. The cuts go at pauses, chosen so that
    /// the pauses cut at are long and each line comes out about as long as its words would take to say (a line's share of
    /// the take goes by its letters, words and punctuation): the longest pauses when the take reads its lines apart, and
    /// still the right ones when it runs them closer together than the commas inside them. Throws when there aren't enough
    /// pauses, or a line comes out far from the length its words suggest.
    /// </summary>
    public static List<Line> Split(float[] samples, int rate, IReadOnlyList<string> script)
    {
        int lines = script.Count;
        int frame = Math.Max(1, (int)(rate * Frame));
        int frames = samples.Length / frame;
        if (frames < 2 || lines < 1)
        {
            throw new ArgumentException("the take is too short to split");
        }

        var db = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int i = f * frame; i < (f + 1) * frame; i++)
            {
                sum += samples[i] * samples[i];
            }

            db[f] = 10f * MathF.Log10((float)(sum / frame) + 1e-12f);
        }

        // Quiet: within 12 dB of the noise floor (the quietest tenth), and at least 35 dB under the loudest.
        float[] sorted = db.Order().ToArray();
        float floor = sorted[frames / 10], loudest = sorted[^1];
        float threshold = MathF.Max(floor + 12f, loudest - 35f);
        bool Loud(int f) => db[f] > threshold;

        int first = Array.FindIndex(db, d => d > threshold), last = Array.FindLastIndex(db, d => d > threshold);
        if (first < 0)
        {
            throw new ArgumentException("the take is silent");
        }

        // Every quiet stretch between the first sound and the last.
        var pauses = new List<(int Start, int Length)>();
        for (int f = first; f <= last; f++)
        {
            if (Loud(f))
            {
                continue;
            }

            int start = f;
            while (f <= last && !Loud(f))
            {
                f++;
            }

            pauses.Add((start, f - start));
        }

        if (pauses.Count < lines - 1)
        {
            throw new ArgumentException($"found {pauses.Count} pauses for {lines} lines");
        }

        List<(int Start, int Length)> cut = Choose(pauses, first, last + 1, script);

        var result = new List<Line>();
        int from = first;
        float before = 0f;
        for (int k = 0; k <= cut.Count; k++)
        {
            int to = k < cut.Count ? cut[k].Start + cut[k].Length / 2 : last + 1;
            // Trim to the sound within [from, to).
            int a = from, b = to - 1;
            while (a < b && !Loud(a))
            {
                a++;
            }

            while (b > a && !Loud(b))
            {
                b--;
            }

            int start = Math.Max(0, a * frame - (int)(Lead * rate));
            int end = Math.Min(samples.Length - 1, (b + 1) * frame + (int)(Tail * rate));
            result.Add(new Line(start, end, before));
            if (k < cut.Count)
            {
                before = cut[k].Length * Frame;
                from = to;
            }
        }

        return result;
    }

    /// <summary>How long a line should take to say, in arbitrary units: its letters, a little more for each word, more for each pause mark.</summary>
    public static float Weight(string line)
    {
        float weight = 0f;
        bool inWord = false;
        foreach (char c in line)
        {
            if (char.IsLetterOrDigit(c))
            {
                weight += 1f;
                weight += inWord ? 0f : 2.5f;
                inWord = true;
            }
            else
            {
                inWord = c is '\'' or '\u2019' && inWord;
                weight += c is ',' or '.' or '!' or '?' or ';' or ':' ? 3f : 0f;
            }
        }

        return MathF.Max(weight, 1f);
    }

    /// <summary>
    /// The pauses to cut at: of every way to pick one less than there are lines, in order, the one scoring best for long
    /// pauses and for lines as long as their words suggest (dynamic programming over the pauses).
    /// </summary>
    private static List<(int Start, int Length)> Choose(List<(int Start, int Length)> pauses, int first, int end, IReadOnlyList<string> script)
    {
        int lines = script.Count, m = pauses.Count;
        if (lines == 1)
        {
            return new List<(int, int)>();
        }

        float[] share = script.Select(Weight).ToArray();
        float total = share.Sum();
        // The speech's length, less the pauses that will most likely be cut at: what the lines share between them.
        float speech = end - first - pauses.OrderByDescending(p => p.Length).Take(lines - 1).Sum(p => p.Length);
        float Expected(int line) => speech * share[line] / total;
        float Middle(int pause) => pauses[pause].Start + pauses[pause].Length / 2f;
        float SegmentCost(int line, float from, float to, float pausesInside)
        {
            float length = to - from - pausesInside;
            float expected = Expected(line);
            float miss = (length - expected) / MathF.Max(expected, 1f);
            return 4f * miss * miss;
        }

        // Quiet frames between two pause middles, not counting the halves of the pauses at the ends.
        float QuietBetween(float from, float to)
        {
            float quiet = 0f;
            foreach ((int start, int length) in pauses)
            {
                float a = MathF.Max(from, start), b = MathF.Min(to, start + length);
                quiet += MathF.Max(0f, b - a);
            }

            return quiet;
        }

        float PauseScore(int pause) => MathF.Log(pauses[pause].Length);

        // best[k, i]: the best score with the k-th cut (0-based) at pause i; from[k, i]: the pause the cut before was at.
        var best = new float[lines - 1, m];
        var back = new int[lines - 1, m];
        for (int i = 0; i < m; i++)
        {
            best[0, i] = PauseScore(i) - SegmentCost(0, first, Middle(i), QuietBetween(first, Middle(i)));
        }

        for (int k = 1; k < lines - 1; k++)
        {
            for (int i = 0; i < m; i++)
            {
                best[k, i] = float.NegativeInfinity;
                for (int j = k - 1; j < i; j++)
                {
                    if (float.IsNegativeInfinity(best[k - 1, j]))
                    {
                        continue;
                    }

                    float score = best[k - 1, j] + PauseScore(i) - SegmentCost(k, Middle(j), Middle(i), QuietBetween(Middle(j), Middle(i)));
                    if (score > best[k, i])
                    {
                        best[k, i] = score;
                        back[k, i] = j;
                    }
                }
            }
        }

        int lastCut = -1;
        float top = float.NegativeInfinity;
        for (int i = lines - 2; i < m; i++)
        {
            if (float.IsNegativeInfinity(best[lines - 2, i]))
            {
                continue;
            }

            float score = best[lines - 2, i] - SegmentCost(lines - 1, Middle(i), end, QuietBetween(Middle(i), end));
            if (score > top)
            {
                top = score;
                lastCut = i;
            }
        }

        var chosen = new List<(int, int)>();
        for (int k = lines - 2, i = lastCut; k >= 0; i = back[k, i], k--)
        {
            chosen.Add(pauses[i]);
        }

        chosen.Reverse();

        // Each line's length against what its words suggest: far off means the take doesn't follow the script.
        float previous = first;
        for (int k = 0; k < lines; k++)
        {
            float next = k < chosen.Count ? chosen[k].Item1 + chosen[k].Item2 / 2f : end;
            float length = next - previous - QuietBetween(previous, next);
            float expected = Expected(k);
            if (length < expected * 0.35f || length > expected * 2.8f)
            {
                throw new ArgumentException(
                    $"line {k + 1} (\"{script[k]}\") comes out {length * Frame:0.00} s long, where its words suggest about {expected * Frame:0.00} s: the take doesn't seem to follow the script");
            }

            previous = next;
        }

        return chosen;
    }

    /// <summary>
    /// A made-up take and its script: <paramref name="lines"/> lines of one to three "words" (shaped noise bursts as long
    /// as their letters), some with a comma whose pause is as long as the shorter pauses between lines, over a faint hiss.
    /// Returns it with where each line truly starts and ends.
    /// </summary>
    public static (float[] Samples, List<(int Start, int End)> Truth, string[] Script) FakeTake(int rate, int lines, int seed)
    {
        var random = new Random(seed);
        var samples = new List<float>();
        var truth = new List<(int, int)>();
        var script = new string[lines];
        void Silence(float seconds)
        {
            for (int i = 0; i < (int)(seconds * rate); i++)
            {
                samples.Add((float)(random.NextDouble() - 0.5) * 0.002f);
            }
        }

        Silence(0.3f);
        for (int line = 0; line < lines; line++)
        {
            int start = samples.Count;
            int words = 1 + random.Next(3);
            var text = new System.Text.StringBuilder();
            for (int w = 0; w < words; w++)
            {
                int letters = 2 + random.Next(6);
                text.Append('a', letters);
                int n = (int)((letters + 2.5f) * 0.045f * rate);
                for (int i = 0; i < n; i++)
                {
                    float envelope = MathF.Sin(MathF.PI * i / n);
                    samples.Add(((float)random.NextDouble() * 2f - 1f) * 0.6f * envelope * envelope +
                                0.3f * MathF.Sin(MathF.Tau * 180f * i / rate) * envelope);
                }

                if (w < words - 1)
                {
                    bool comma = random.Next(2) == 0;
                    text.Append(comma ? ", " : " ");
                    Silence(comma ? 0.22f + 0.06f * (float)random.NextDouble() : 0.05f + 0.05f * (float)random.NextDouble());
                }
            }

            text.Append('!');
            script[line] = text.ToString();
            truth.Add((start, samples.Count));
            Silence(line < lines - 1 ? 0.26f + 0.12f * (float)random.NextDouble() : 0.4f);
        }

        return (samples.ToArray(), truth, script);
    }
}
