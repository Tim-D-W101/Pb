using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.World;
using Pb.Sim.Gear;

namespace Pb.Game.Player;

/// <summary>
/// The brands' masks, built in code and fitted to each character's face (<see cref="ClothesZones"/>): the face's own
/// vertices give the shell's reach in each direction round it and at each height (a grid of radii about an upright
/// axis just behind where the face is cut away, filled, swollen and smoothed so it never dips into the face), and each
/// brand's parts are laid on that surface a little out from it: Kilnmark's Visor a boxy goggle frame with a flat lens,
/// a brim, a slotted chin guard and bolted ear pieces; Vellis's Arc a tall wraparound lens, side fins and a pointed
/// lower face with slits; Quarrow's Thicket a frame and lens over a mesh lower face, a tall forehead guard and ear
/// covers. A strap runs round the back of the head with the mark on it. Built in the head's frame (+X right, +Y up,
/// −Z forward, metres, the Head joint at the origin), once per mask and character, and shared.
/// </summary>
public static class MaskShapes
{
    private static readonly Dictionary<(int Item, string Path), ArrayMesh?> Built = new();

    /// <summary>The shell of mask <paramref name="item"/> fitted to the model at <paramref name="path"/>, or null for the model's own mask.</summary>
    public static ArrayMesh? Shell(GearItem item, string path, ClothesZones zones, float gap)
    {
        if (MaskRecipes.IsOwn(item.Shape) || zones.FacePoints.Count < 30)
        {
            return null;
        }

        if (Built.TryGetValue((item.Index, path), out ArrayMesh? built))
        {
            return built;
        }

        var fit = new Fit(zones, gap);
        var m = new ShapeMesh();
        switch (item.Shape)
        {
            case "mask_arc":
                Arc(m, fit, item.Brand);
                break;
            case "mask_thicket":
                Thicket(m, fit, item.Brand);
                break;
            default:
                Visor(m, fit, item.Brand);
                break;
        }

        var mesh = new ArrayMesh();
        m.Commit(mesh, GearModels.Material);
        built = ShapeMesh.WithLods(mesh);
        Built[(item.Index, path)] = built;
        return built;
    }

    private const float Deg = Mathf.Pi / 180f;

    private static void Visor(ShapeMesh m, Fit f, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Plastic, GearPaint.Main), second = GearZone.Of(GearFinish.Plastic, GearPaint.Second);
        // The chin guard, slotted across the mouth.
        f.Band(m, second, -100f, 100f, 0f, 0.5f, 0f, 0.006f);
        for (int k = 0; k < 3; k++)
        {
            float h = 0.15f + 0.09f * k;
            f.Band(m, GearZone.DarkSteel, -24f, 24f, h, h + 0.03f, 0.006f, 0.0072f);
        }

        // The goggle frame round a flat lens, a brow piece above with a brim, bolted ear pieces.
        Frame(m, f, main, 0.5f, 0.86f, 0.07f, 76f, 104f, 0.002f, 0.011f);
        f.Band(m, GearZone.Lens, -76f, 76f, 0.57f, 0.79f, 0.001f, 0.005f, upright: (0.5f, 0.86f));
        f.Band(m, main, -100f, 100f, 0.86f, 1f, 0f, 0.007f, upright: (0.5f, 1f));
        f.Ledge(m, main, -62f, 62f, 0.86f, 0.011f, 0.038f, 0.005f, upright: (0.5f, 0.86f));
        foreach (float side in new[] { -1f, 1f })
        {
            f.Band(m, main, side > 0f ? 92f : -112f, side > 0f ? 112f : -92f, 0.42f, 0.9f, 0.006f, 0.016f, upright: (0.42f, 0.9f));
            f.Bolt(m, GearZone.Steel, side * 102f, 0.68f, 0.016f, 0.006f, upright: (0.42f, 0.9f));
        }

        Strap(m, f, brand, 0.68f, 0.032f, 108f);
    }

    private static void Arc(ShapeMesh m, Fit f, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Gloss, GearPaint.Main), second = GearZone.Of(GearFinish.Gloss, GearPaint.Second);
        // The lower face, coming to a point down the middle, with slits each side.
        float Ridge(float theta, float h) => 0.014f * MathF.Max(0f, 1f - MathF.Abs(theta) / 62f) * MathF.Sin(Mathf.Pi * Mathf.Clamp(h / 0.46f, 0f, 1f));
        f.Band(m, main, -98f, 98f, 0f, 0.46f, 0f, 0.007f, Ridge);
        foreach (float side in new[] { -1f, 1f })
        {
            for (int k = 0; k < 3; k++)
            {
                float h = 0.12f + 0.1f * k;
                f.Band(m, second, side > 0f ? 14f : -32f, side > 0f ? 32f : -14f, h, h + 0.045f, 0.0072f, 0.0085f, Ridge);
            }
        }

        // A tall wraparound lens in a thin frame, fins at the sides, a brow piece.
        Frame(m, f, main, 0.46f, 0.9f, 0.06f, 92f, 104f, 0.002f, 0.009f);
        f.Band(m, GearZone.Lens, -92f, 92f, 0.52f, 0.84f, 0.002f, 0.006f, upright: (0.46f, 0.9f));
        foreach (float side in new[] { -1f, 1f })
        {
            f.Band(m, second, side > 0f ? 96f : -116f, side > 0f ? 116f : -96f, 0.5f, 0.8f, 0.009f, 0.014f, upright: (0.46f, 0.9f));
        }

        f.Band(m, main, -100f, 100f, 0.9f, 1.02f, 0f, 0.006f, upright: (0.46f, 1.02f));
        Strap(m, f, brand, 0.7f, 0.026f, 112f);
    }

    private static void Thicket(ShapeMesh m, Fit f, GearBrand brand)
    {
        int main = GearZone.Of(GearFinish.Plastic, GearPaint.Main), second = GearZone.Of(GearFinish.Plastic, GearPaint.Second);
        // The lower face: a mesh over a black backing.
        f.Band(m, GearZone.Rubber, -96f, 96f, 0f, 0.48f, 0f, 0.004f);
        for (float h = 0.03f; h < 0.47f; h += 0.075f)
        {
            f.Band(m, second, -96f, 96f, h, h + 0.022f, 0.004f, 0.0068f);
        }

        for (float theta = -90f; theta <= 90f; theta += 12f)
        {
            f.Band(m, second, theta - 1.6f, theta + 1.6f, 0f, 0.48f, 0.004f, 0.0068f);
        }

        // A frame and lens, a tall guard over the forehead with a lip, and ear covers.
        Frame(m, f, main, 0.48f, 0.88f, 0.07f, 78f, 104f, 0.002f, 0.011f);
        f.Band(m, GearZone.Lens, -78f, 78f, 0.55f, 0.81f, 0.001f, 0.005f, upright: (0.48f, 0.88f));
        f.Band(m, main, -102f, 102f, 0.88f, 1.12f, 0.004f, 0.012f, upright: (0.48f, 1.12f));
        f.Ledge(m, main, -70f, 70f, 1.1f, 0.012f, 0.022f, 0.006f, upright: (0.48f, 1.12f));
        foreach (float side in new[] { -1f, 1f })
        {
            f.Band(m, second, side > 0f ? 84f : -114f, side > 0f ? 114f : -84f, 0.3f, 0.84f, 0.008f, 0.017f, upright: (0.3f, 0.84f));
        }

        Strap(m, f, brand, 0.68f, 0.034f, 110f);
    }

    /// <summary>A goggle frame: rims along its bottom and top (each <paramref name="rim"/> of the face's height) and pieces at the sides from <paramref name="inner"/>° to <paramref name="outer"/>°.</summary>
    private static void Frame(ShapeMesh m, Fit f, int zone, float bottom, float top, float rim, float inner, float outer, float o0, float o1)
    {
        // Goggles stand upright over the eyes, however the face beneath slopes.
        (float, float) upright = (bottom, top);
        f.Band(m, zone, -outer, outer, bottom, bottom + rim, o0, o1, upright: upright);
        f.Band(m, zone, -outer, outer, top - rim, top, o0, o1, upright: upright);
        f.Band(m, zone, inner, outer, bottom + rim, top - rim, o0, o1, upright: upright);
        f.Band(m, zone, -outer, -inner, bottom + rim, top - rim, o0, o1, upright: upright);
    }

    /// <summary>The strap round the back of the head at <paramref name="h"/> of the face's height from the shell's sides at <paramref name="side"/>°, with the brand's mark on both sides.</summary>
    private static void Strap(ShapeMesh m, Fit f, GearBrand brand, float h, float height, float side)
    {
        int strap = GearZone.Of(GearFinish.Fabric, GearPaint.Accent), mark = GearZone.Of(GearFinish.Mark, GearPaint.Main);
        f.StrapRing(m, strap, h, height, 0.004f, side);
        f.StrapMarks(m, mark, brand, height * 0.95f);
    }

    /// <summary>
    /// The face's shape round an upright axis 4 cm behind where the face is cut away: the shell's radius at each angle
    /// round it (0 straight ahead, positive towards the right) and each height (0 the bottom of the chin, 1 the brow),
    /// from the face's vertices plus the gap; and the head's round the strap's axis at its middle.
    /// </summary>
    private sealed class Fit
    {
        private const int Rows = 12, Sectors = 40;
        private const float Reach = 125f * Deg;
        private readonly float[,] _radius = new float[Rows + 1, Sectors + 1];
        private readonly ClothesZones _zones;
        private readonly float _gap;
        private readonly float _axis;
        private readonly float _low, _high;

        public Fit(ClothesZones zones, float gap)
        {
            _zones = zones;
            _gap = gap;
            _axis = zones.FaceFrom - 0.04f;
            float low = float.MaxValue;
            foreach (Vector3 p in zones.FacePoints)
            {
                low = MathF.Min(low, p.Y);
            }

            _low = low - gap;
            _high = zones.FaceBelow + 0.02f;
            // Each row's typical reach, so a stray strand of hair well out from the rest doesn't swell the shell.
            var rowReach = new List<float>[Rows + 1];
            for (int row = 0; row <= Rows; row++)
            {
                rowReach[row] = new List<float>();
            }

            foreach (Vector3 p in zones.FacePoints)
            {
                rowReach[Row(p.Y)].Add(Polar(p).R);
            }

            var limit = new float[Rows + 1];
            for (int row = 0; row <= Rows; row++)
            {
                rowReach[row].Sort();
                limit[row] = rowReach[row].Count > 0 ? rowReach[row][rowReach[row].Count / 2] * 1.35f : float.MaxValue;
            }

            var found = new float[Rows + 1, Sectors + 1];
            foreach (Vector3 p in zones.FacePoints)
            {
                (float theta, float r) = Polar(p);
                int row = Row(p.Y);
                int sector = Mathf.Clamp(Mathf.RoundToInt((theta + Reach) / (2f * Reach) * Sectors), 0, Sectors);
                if (r <= limit[row])
                {
                    found[row, sector] = MathF.Max(found[row, sector], r);
                }
            }

            // Cells the face has nothing in take the nearest along their row, then rows with nothing the nearest row.
            for (int row = 0; row <= Rows; row++)
            {
                for (int s = 0; s <= Sectors; s++)
                {
                    if (found[row, s] > 0f)
                    {
                        continue;
                    }

                    for (int d = 1; d <= Sectors && found[row, s] <= 0f; d++)
                    {
                        float a = s - d >= 0 ? found[row, s - d] : 0f, b = s + d <= Sectors ? found[row, s + d] : 0f;
                        found[row, s] = MathF.Max(a, b) * (a > 0f || b > 0f ? 1f : 0f);
                    }
                }
            }

            for (int row = 0; row <= Rows; row++)
            {
                for (int d = 1; d <= Rows && found[row, 0] <= 0f; d++)
                {
                    int from = row - d >= 0 && found[row - d, 0] > 0f ? row - d : row + d <= Rows && found[row + d, 0] > 0f ? row + d : -1;
                    if (from >= 0)
                    {
                        for (int s = 0; s <= Sectors; s++)
                        {
                            found[row, s] = found[from, s];
                        }
                    }
                }
            }

            // Swollen by a cell each way (so a shell between grid points never dips into the face), then smoothed,
            // never below what it covers.
            var swollen = new float[Rows + 1, Sectors + 1];
            for (int row = 0; row <= Rows; row++)
            {
                for (int s = 0; s <= Sectors; s++)
                {
                    float most = 0f;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int ds = -1; ds <= 1; ds++)
                        {
                            int r = Math.Clamp(row + dr, 0, Rows), c = Math.Clamp(s + ds, 0, Sectors);
                            most = MathF.Max(most, found[r, c]);
                        }
                    }

                    swollen[row, s] = most;
                }
            }

            for (int row = 0; row <= Rows; row++)
            {
                for (int s = 0; s <= Sectors; s++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int ds = -2; ds <= 2; ds++)
                        {
                            int r = Math.Clamp(row + dr, 0, Rows), c = Math.Clamp(s + ds, 0, Sectors);
                            sum += swollen[r, c];
                            n++;
                        }
                    }

                    _radius[row, s] = MathF.Max(sum / n, found[row, s]) + gap;
                }
            }
        }

        private int Row(float y) => Mathf.Clamp(Mathf.RoundToInt((y - _low) / (_high - _low) * Rows), 0, Rows);

        /// <summary>A point of the head's frame about the axis: its angle (0 ahead, + to the right) and its distance off it.</summary>
        private (float Theta, float R) Polar(Vector3 p)
        {
            float x = p.X, ahead = -p.Z - _axis;
            return (MathF.Atan2(x, ahead), MathF.Sqrt(x * x + ahead * ahead));
        }

        private float Radius(float theta, float h)
        {
            float fr = Mathf.Clamp(h, 0f, 1f) * Rows, fs = Mathf.Clamp((theta + Reach) / (2f * Reach), 0f, 1f) * Sectors;
            int r0 = Math.Min((int)fr, Rows - 1), s0 = Math.Min((int)fs, Sectors - 1);
            float tr = fr - r0, ts = fs - s0;
            float a = Mathf.Lerp(_radius[r0, s0], _radius[r0, s0 + 1], ts), b = Mathf.Lerp(_radius[r0 + 1, s0], _radius[r0 + 1, s0 + 1], ts);
            return Mathf.Lerp(a, b, tr);
        }

        /// <summary>The shell's reach at <paramref name="theta"/>, the furthest it gets anywhere from <paramref name="h0"/> to <paramref name="h1"/> up: a part standing upright over that height.</summary>
        private float Upright(float theta, float h0, float h1)
        {
            float most = 0f;
            for (int i = 0; i <= 8; i++)
            {
                most = MathF.Max(most, Radius(theta, Mathf.Lerp(h0, h1, i / 8f)));
            }

            return most;
        }

        /// <summary>
        /// The point on the shell at angle <paramref name="theta"/> (rad) and height <paramref name="h"/>, <paramref name="offset"/> out
        /// from it; with <paramref name="upright"/>, standing straight up over that span of heights instead of following the face.
        /// </summary>
        public Vector3 At(float theta, float h, float offset, (float From, float To)? upright = null)
        {
            float r = (upright is { } span ? Upright(theta, span.From, span.To) : Radius(theta, h)) + offset;
            float y = Mathf.Lerp(_low, _high, h);
            return new Vector3(MathF.Sin(theta) * r, y, -(_axis + MathF.Cos(theta) * r));
        }

        private Vector3 Normal(float theta, float h, float offset, (float From, float To)? upright = null)
        {
            const float e = 0.01f;
            Vector3 along = At(theta + e, h, offset, upright) - At(theta - e, h, offset, upright);
            Vector3 up = At(theta, h + e, offset, upright) - At(theta, h - e, offset, upright);
            Vector3 n = up.Cross(along);
            return n.LengthSquared() > 1e-12f ? n.Normalized() : new Vector3(MathF.Sin(theta), 0f, -MathF.Cos(theta));
        }

        /// <summary>
        /// A slab on the shell from <paramref name="from"/>° to <paramref name="to"/>° round and <paramref name="h0"/> to
        /// <paramref name="h1"/> up, between <paramref name="o0"/> and <paramref name="o1"/> out from it (and <paramref name="extra"/>
        /// more, by angle and height): its outer and inner faces and its four edges.
        /// </summary>
        public void Band(ShapeMesh m, int zone, float from, float to, float h0, float h1, float o0, float o1, Func<float, float, float>? extra = null,
            (float From, float To)? upright = null)
        {
            int na = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(to - from) / 6f));
            int nh = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(h1 - h0) / 0.08f));
            float Theta(int i) => Mathf.Lerp(from, to, (float)i / na) * Deg;
            float H(int j) => Mathf.Lerp(h0, h1, (float)j / nh);
            float Out(int i, int j, float o) => o + (extra?.Invoke(Mathf.Lerp(from, to, (float)i / na), H(j)) ?? 0f);
            Vector3 P(int i, int j, float o) => At(Theta(i), H(j), Out(i, j, o), upright);
            Vector3 N(int i, int j, float o) => Normal(Theta(i), H(j), Out(i, j, o), upright);
            for (int i = 0; i < na; i++)
            {
                for (int j = 0; j < nh; j++)
                {
                    Quad(m, zone, P(i, j, o1), P(i + 1, j, o1), P(i + 1, j + 1, o1), P(i, j + 1, o1), N(i, j, o1), N(i + 1, j, o1), N(i + 1, j + 1, o1), N(i, j + 1, o1), 1f);
                    Quad(m, zone, P(i, j, o0), P(i + 1, j, o0), P(i + 1, j + 1, o0), P(i, j + 1, o0), -N(i, j, o0), -N(i + 1, j, o0), -N(i + 1, j + 1, o0), -N(i, j + 1, o0), -1f);
                }

                // The top and bottom edges.
                Edge(m, zone, P(i, nh, o0), P(i + 1, nh, o0), P(i + 1, nh, o1), P(i, nh, o1), P(i, nh, o0) - P(i, nh - 1, o0));
                Edge(m, zone, P(i, 0, o0), P(i + 1, 0, o0), P(i + 1, 0, o1), P(i, 0, o1), P(i, 0, o0) - P(i, 1, o0));
            }

            for (int j = 0; j < nh; j++)
            {
                Edge(m, zone, P(0, j, o0), P(0, j + 1, o0), P(0, j + 1, o1), P(0, j, o1), P(0, j, o0) - P(1, j, o0));
                Edge(m, zone, P(na, j, o0), P(na, j + 1, o0), P(na, j + 1, o1), P(na, j, o1), P(na, j, o0) - P(na - 1, j, o0));
            }
        }

        /// <summary>A brim jutting out from the shell at height <paramref name="h"/>: a flat strip from <paramref name="o0"/> to <paramref name="o1"/> out, <paramref name="thickness"/> thick.</summary>
        public void Ledge(ShapeMesh m, int zone, float from, float to, float h, float o0, float o1, float thickness, (float From, float To)? upright = null)
        {
            float lift = thickness / MathF.Max(_high - _low, 0.05f);
            Band(m, zone, from, to, h, h + lift, o0, o1, upright: upright);
        }

        /// <summary>A bolt head on the shell, facing out.</summary>
        public void Bolt(ShapeMesh m, int zone, float theta, float h, float offset, float radius, (float From, float To)? upright = null)
        {
            float t = theta * Deg;
            Vector3 n = Normal(t, h, offset, upright);
            m.Cylinder(zone, At(t, h, offset, upright) + n * 0.001f, ShapeMesh.BasisAlong(n), radius, 0.003f, 8);
        }

        /// <summary>
        /// The strap: round the back of the head at <paramref name="h"/> of the face's height, <paramref name="height"/>
        /// tall, from the shell's sides at <paramref name="side"/>° round its axis to the back, fitted round the head's
        /// vertices there (and never inside the shell where it starts).
        /// </summary>
        public void StrapRing(ShapeMesh m, int zone, float h, float height, float thickness, float side)
        {
            float y = Mathf.Lerp(_low, _high, h);
            float[] reach = StrapReach(y, height, side * Deg, h);
            int n = reach.Length;
            float Angle(int k) => -Mathf.Pi + Mathf.Tau * k / n;
            Vector3 P(int k, float dy, float o)
            {
                float a = Angle(k), r = reach[((k % n) + n) % n] + o;
                return new Vector3(MathF.Sin(a) * r, y + dy, -(_axis + MathF.Cos(a) * r));
            }

            float from = (side - 6f) * Deg;
            float hy = height * 0.5f;
            for (int k = 0; k < n; k++)
            {
                if (MathF.Abs(Angle(k)) < from || MathF.Abs(Angle(k + 1)) < from)
                {
                    continue;
                }

                Vector3 n0 = (P(k, 0f, 1f) - P(k, 0f, 0f)).Normalized(), n1 = (P(k + 1, 0f, 1f) - P(k + 1, 0f, 0f)).Normalized();
                Quad(m, zone, P(k, -hy, thickness), P(k + 1, -hy, thickness), P(k + 1, hy, thickness), P(k, hy, thickness), n0, n1, n1, n0, 1f);
                Quad(m, zone, P(k, -hy, 0f), P(k + 1, -hy, 0f), P(k + 1, hy, 0f), P(k, hy, 0f), -n0, -n1, -n1, -n0, -1f);
                Edge(m, zone, P(k, hy, 0f), P(k + 1, hy, 0f), P(k + 1, hy, thickness), P(k, hy, thickness), Vector3.Up);
                Edge(m, zone, P(k, -hy, 0f), P(k + 1, -hy, 0f), P(k + 1, -hy, thickness), P(k, -hy, thickness), Vector3.Down);
            }

            _strap = (reach, y, thickness, side);
        }

        private (float[] Reach, float Y, float Thickness, float Side)? _strap;

        /// <summary>The brand's mark on each side of the strap, halfway round from the shell to the back, wrapped round it.</summary>
        public void StrapMarks(ShapeMesh m, int zone, GearBrand brand, float height)
        {
            if (_strap is not { } strap || !BrandMarks.Row(brand, out Rect2 uv, out float aspect))
            {
                return;
            }

            float[] reach = strap.Reach;
            int n = reach.Length;
            float R(float a)
            {
                float f = Mathf.PosMod((a + Mathf.Pi) / Mathf.Tau * n, n);
                int k = (int)f;
                return Mathf.Lerp(reach[k % n], reach[(k + 1) % n], f - k) + strap.Thickness + 0.0008f;
            }

            Vector3 P(float a, float dy) => new(MathF.Sin(a) * R(a), strap.Y + dy, -(_axis + MathF.Cos(a) * R(a)));
            foreach (float sign in new[] { -1f, 1f })
            {
                // Each mark keeps to its own side: no longer than most of the strap from the shell to the back.
                float centre = sign * (strap.Side * Deg + Mathf.Pi) * 0.5f;
                float room = (Mathf.Pi - strap.Side * Deg) * 0.8f * R(centre);
                height = MathF.Min(height, room / aspect);
                float span = height * aspect / MathF.Max(R(centre), 0.05f);
                // Read from outside, the letters run towards smaller angles: forwards on the right, backwards on the left.
                float start = centre + span * 0.5f, end = centre - span * 0.5f;
                const int strips = 10;
                float hy = height * 0.5f;
                for (int i = 0; i < strips; i++)
                {
                    float a0 = Mathf.Lerp(start, end, (float)i / strips), a1 = Mathf.Lerp(start, end, (float)(i + 1) / strips);
                    float u0 = Mathf.Lerp(uv.Position.X, uv.End.X, (float)i / strips), u1 = Mathf.Lerp(uv.Position.X, uv.End.X, (float)(i + 1) / strips);
                    Vector3 normal = new Vector3(MathF.Sin(a0) + MathF.Sin(a1), 0f, -(MathF.Cos(a0) + MathF.Cos(a1))).Normalized();
                    Vector3 tl = P(a0, hy), tr = P(a1, hy), br = P(a1, -hy), bl = P(a0, -hy);
                    if ((tr - tl).Cross(bl - tl).Dot(normal) > 0f)
                    {
                        m.Tri(zone, tl, bl, tr, normal, new Vector2(u0, uv.Position.Y), new Vector2(u0, uv.End.Y), new Vector2(u1, uv.Position.Y));
                        m.Tri(zone, tr, bl, br, normal, new Vector2(u1, uv.Position.Y), new Vector2(u0, uv.End.Y), new Vector2(u1, uv.End.Y));
                    }
                    else
                    {
                        m.Tri(zone, tl, tr, bl, normal, new Vector2(u0, uv.Position.Y), new Vector2(u1, uv.Position.Y), new Vector2(u0, uv.End.Y));
                        m.Tri(zone, tr, br, bl, normal, new Vector2(u1, uv.Position.Y), new Vector2(u1, uv.End.Y), new Vector2(u0, uv.End.Y));
                    }
                }
            }
        }

        /// <summary>
        /// How far the head reaches round the shell's axis at height <paramref name="y"/> (within the strap's height), in 36
        /// directions from straight back round to the front, plus the gap; never inside the shell from its sides forward.
        /// </summary>
        private float[] StrapReach(float y, float height, float side, float h)
        {
            const int n = 36;
            var reach = new float[n];
            foreach (Vector3 p in _zones.HeadPoints)
            {
                if (MathF.Abs(p.Y - y) > height)
                {
                    continue;
                }

                (float theta, float r) = Polar(p);
                int k = ((int)MathF.Round((theta + Mathf.Pi) / Mathf.Tau * n) % n + n) % n;
                reach[k] = MathF.Max(reach[k], r);
            }

            // Where it meets the shell, it's no further in than the shell's sides.
            for (int k = 0; k < n; k++)
            {
                float a = -Mathf.Pi + Mathf.Tau * k / n;
                if (MathF.Abs(a) <= side + 0.15f && MathF.Abs(a) <= Reach)
                {
                    reach[k] = MathF.Max(reach[k], Radius(a, h) - _gap);
                }
            }

            float[] filled = (float[])reach.Clone();
            for (int k = 0; k < n; k++)
            {
                for (int d = 1; d < n && filled[k] <= 0f; d++)
                {
                    filled[k] = MathF.Max(reach[(k + d) % n], reach[(k - d + n) % n]);
                }
            }

            var smooth = new float[n];
            for (int k = 0; k < n; k++)
            {
                smooth[k] = MathF.Max(filled[k], (filled[(k + n - 1) % n] + filled[k] * 2f + filled[(k + 1) % n]) * 0.25f) + _gap;
            }

            return smooth;
        }

        /// <summary>A quad with a normal per corner, wound so it faces the way <paramref name="side"/> says (out from the head: +1).</summary>
        private static void Quad(ShapeMesh m, int zone, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, float side)
        {
            // The normals given already face the way the quad should; side only says which surface it is.
            _ = side;
            Vector2 Uv(Vector3 p) => new(p.X + p.Z, p.Y);
            Vector3 facing = na + nb + nc + nd;
            if ((b - a).Cross(c - a).Dot(facing) > 0f)
            {
                m.Smooth(zone, a, na, Uv(a), c, nc, Uv(c), b, nb, Uv(b));
                m.Smooth(zone, a, na, Uv(a), d, nd, Uv(d), c, nc, Uv(c));
            }
            else
            {
                m.Smooth(zone, a, na, Uv(a), b, nb, Uv(b), c, nc, Uv(c));
                m.Smooth(zone, a, na, Uv(a), c, nc, Uv(c), d, nd, Uv(d));
            }
        }

        /// <summary>A flat edge facing <paramref name="outward"/>.</summary>
        private static void Edge(ShapeMesh m, int zone, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Vector3 n = outward.LengthSquared() > 1e-12f ? outward.Normalized() : Vector3.Up;
            Quad(m, zone, a, b, c, d, n, n, n, n, 1f);
        }
    }
}
