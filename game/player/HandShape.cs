using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.World;

namespace Pb.Game.Player;

/// <summary>The materials of the first-person hands: glove, glove cuff and sleeve (ids clear of <see cref="MarkerPart"/>).</summary>
public static class HandPart
{
    public const int Glove = 20, Cuff = 21, Sleeve = 22;
}

/// <summary>
/// Gloved hands holding the marker, built in code in the marker's frame (see <see cref="MarkerShape"/>):
/// the trigger hand round the pistol grip (the palm on its back and right side, three fingers
/// wrapping round its front, the index finger through the guard on the trigger, the thumb over the
/// top) and the support hand round the foregrip (four fingers round it from the left), each with a
/// cuff and a sleeved forearm running back out of view. First person only: opponents' hands are
/// their models'.
/// </summary>
public static class HandShape
{
    private const float Finger = 0.0095f, Thumb = 0.0105f;

    /// <summary>The top of the foregrip, where the support hand holds it (marker frame): the hand turns about it.</summary>
    public static Vector3 ForegripTop => At(0.078f, -0.036f);

    /// <summary>The middle of the pistol grip's top, just under the frame, where the trigger hand holds it (marker frame).</summary>
    public static Vector3 PistolGripTop => At(-0.069f, -0.058f);

    /// <summary>Both hands on the marker.</summary>
    public static void Build(ShapeMesh m)
    {
        BuildTrigger(m);
        BuildSupport(m);
    }

    /// <summary>The trigger hand round the pistol grip.</summary>
    public static void BuildTrigger(ShapeMesh m)
    {
        // The pistol grip: from the middle of its top (just under the frame) down its rake.
        Grip trigger = new(PistolGripTop, Direction(-0.028f, -0.097f), Vector3.Right);
        Hand(m, trigger, fingers: new[] { 0.03f, 0.052f, 0.074f }, wrist: new Vector3(0.4f, -0.45f, 0.8f), reach: 0.36f);
        // The index finger runs forward from the top of the hand, through the guard, onto the trigger.
        Vector3 knuckle = trigger.At(0.006f, 0.014f, 0.027f);
        Chain(m, HandPart.Glove, Finger * 0.95f, knuckle, Mark(-0.03f, -0.07f, 0.02f), Mark(-0.011f, -0.075f, 0.011f), Mark(-0.012f, -0.087f, 0.002f));
        // The thumb wraps over the left of the grip's top.
        Chain(m, HandPart.Glove, Thumb, trigger.At(0.012f, -0.03f, 0.006f), trigger.At(0.0f, -0.02f, -0.022f), trigger.At(0.008f, 0.004f, -0.03f), trigger.At(0.02f, 0.018f, -0.026f));

    }

    /// <summary>The support hand round the foregrip.</summary>
    public static void BuildSupport(ShapeMesh m)
    {
        // The foregrip, held from the left, all four fingers round it.
        Grip support = new(ForegripTop, Direction(-0.008f, -0.097f), Vector3.Left);
        Hand(m, support, fingers: new[] { 0.012f, 0.033f, 0.054f, 0.075f }, wrist: new Vector3(-0.55f, -0.45f, 0.7f), reach: 0.5f);
        Chain(m, HandPart.Glove, Thumb, support.At(0.004f, -0.026f, 0.004f), support.At(-0.004f, -0.008f, -0.022f), support.At(0.006f, 0.014f, -0.026f));
    }

    /// <summary>
    /// The support hand holding a pod by its lower half, posed as it tips the pod into the loader: the
    /// pod's open end at <paramref name="mouth"/>, pointing along <paramref name="pour"/>, the arm coming up
    /// from below on the left. The pod is a smoked tube full of paint (<paramref name="balls"/> of it) with
    /// its lid flipped open. Returns where the hand grips it, for turning the whole about.
    /// </summary>
    public static Vector3 BuildPodHand(ShapeMesh m, Vector3 mouth, Vector3 pour, int balls)
    {
        pour = pour.Normalized();
        const float length = 0.19f, radius = 0.024f;
        Vector3 bottom = mouth - pour * length;
        // The tube, its rim at the mouth, and the lid hinged open beside it.
        var tube = new List<Vector2> { new(0f, 0f), new(radius * 0.9f, 0f), new(radius, 0.006f), new(radius, length - 0.004f), new(radius * 1.08f, length), new(radius * 0.9f, length) };
        m.Lathe((int)MarkerPart.Shell, bottom, ShapeMesh.BasisAlong(pour), tube, 14);
        Vector3 side = pour.Cross(Vector3.Back).Normalized();
        m.Pillow((int)MarkerPart.Lid, mouth + side * radius * 1.6f - pour * 0.006f, new Vector3(0.05f, 0.012f, 0.05f), ShapeMesh.BasisAlong(side), 3f, 4, 12);
        // Paint in it, three balls a layer, from the bottom up to a little short of the mouth.
        const float ball = 0.0087f;
        int layers = Math.Max(1, balls / 3);
        Vector3 a = pour.Cross(Vector3.Up).LengthSquared() > 1e-4f ? pour.Cross(Vector3.Up).Normalized() : Vector3.Right, b = pour.Cross(a);
        for (int layer = 0; layer < layers; layer++)
        {
            float along = 0.012f + layer * (length - 0.05f) / Math.Max(1, layers - 1);
            for (int k = 0; k < 3; k++)
            {
                float angle = Mathf.Tau * k / 3f + layer * 1.05f;
                Vector3 at = bottom + pour * along + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * (radius - ball - 0.002f);
                m.Pillow((int)MarkerPart.Paint, at, new Vector3(ball, ball, ball) * 2f, Basis.Identity, 2f, 5, 8);
            }
        }

        // Held from the left round its lower half, the back of the hand down towards you.
        Vector3 top = mouth - pour * 0.085f;
        Grip pod = new(top, pour * -1f, side);
        Hand(m, pod, fingers: new[] { 0.012f, 0.033f, 0.054f, 0.075f }, wrist: new Vector3(-0.3f, -0.5f, 0.81f), reach: 0.5f);
        Chain(m, HandPart.Glove, Thumb, pod.At(0.004f, -0.026f, 0.004f), pod.At(-0.004f, -0.008f, -0.022f), pod.At(0.006f, 0.014f, -0.026f));
        return top;
    }

    /// <summary>
    /// A palm on the grip's back and outer side, fingers wrapping round its front to the inner side, the
    /// wrist and the arm, running along <paramref name="wrist"/> (marker frame: right, up, back) for <paramref name="reach"/>.
    /// </summary>
    private static void Hand(ShapeMesh m, Grip g, float[] fingers, Vector3 wrist, float reach)
    {
        // Pillow axes: X out the back of the hand, Y up the grip, Z out of the palm's back.
        var palm = new Basis(g.Out, -g.Down, g.Out.Cross(-g.Down));
        m.Pillow(HandPart.Glove, g.At(0.05f, -0.028f, 0.017f), new Vector3(0.04f, 0.1f, 0.05f), palm, 2.6f, 6, 10);
        foreach (float a in fingers)
        {
            Chain(m, HandPart.Glove, Finger, g.At(a, 0.008f, 0.028f), g.At(a, 0.034f, 0.013f), g.At(a, 0.033f, -0.011f), g.At(a, 0.012f, -0.026f));
        }

        // The wrist under the heel of the hand, then the cuff and the sleeve, back and away out of view.
        Vector3 from = g.At(0.09f, -0.03f, 0.024f);
        Vector3 along = wrist.Normalized();
        m.Pillow(HandPart.Glove, from, new Vector3(0.055f, 0.05f, 0.055f), ShapeMesh.BasisAlong(along), 2.2f, 5, 10);
        Taper(m, HandPart.Cuff, from + along * 0.005f, along, 0.032f, 0.036f, 0.065f);
        Taper(m, HandPart.Sleeve, from + along * 0.06f, along, 0.04f, 0.052f, reach);
    }

    /// <summary>A rounded tube through the points: rods with a ball at each joint and at the tip.</summary>
    private static void Chain(ShapeMesh m, int material, float radius, params Vector3[] points)
    {
        var ball = new Vector3(radius, radius, radius) * 2f;
        for (int i = 0; i < points.Length; i++)
        {
            float shrink = 1f - 0.12f * i / Math.Max(1, points.Length - 1);
            m.Pillow(material, points[i], ball * shrink, Basis.Identity, 2f, 4, 8);
            if (i + 1 < points.Length)
            {
                m.Rod(material, points[i], points[i + 1], radius * shrink, 8, caps: false);
            }
        }
    }

    /// <summary>A tapered sleeve from <paramref name="start"/> along <paramref name="along"/>.</summary>
    private static void Taper(ShapeMesh m, int material, Vector3 start, Vector3 along, float r0, float r1, float length)
    {
        var profile = new List<Vector2> { new(0f, 0f), new(r0, 0f), new(r0, 0f), new(r1, length), new(r1, length), new(0f, length) };
        m.Lathe(material, start, ShapeMesh.BasisAlong(along), profile, 12);
    }

    private static Vector3 At(float forward, float up, float right = 0f) => new(right, up, -forward);

    private static Vector3 Mark(float forward, float up, float right) => At(forward, up, right);

    /// <summary>A unit vector in the marker's side plane: so far forward, so far up.</summary>
    private static Vector3 Direction(float forward, float up) => At(forward, up).Normalized();

    /// <summary>
    /// A grip's frame: its top, the way down it, and the side the palm wraps (the hand's outside);
    /// points are given as (down it, towards its front, towards the outside).
    /// </summary>
    private readonly record struct Grip(Vector3 Top, Vector3 Down, Vector3 Out)
    {
        public Vector3 Front => Down.Cross(Out).Normalized() * (Out.X > 0f ? -1f : 1f);

        public Vector3 At(float along, float front, float outside) => Top + Down * along + Front * front + Out * outside;
    }
}
