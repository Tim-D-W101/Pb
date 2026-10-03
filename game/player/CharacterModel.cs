using System;
using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim.Collision;

namespace Pb.Game.Player;

/// <summary>
/// A rigged character model from the art pipeline standing in for the hitbox boxes. Its idle clip
/// loops, the shared movement clips (fitted to its build by <see cref="MoveClipBaker"/>) play over it
/// as its <see cref="Gait"/> says, a <see cref="CharacterPoser"/> bends it to the sim's pose, armbands
/// in the team colour sit on both upper arms, and bone attachments give splats something to stick to.
/// Gear (marker, loader, tank) is drawn by <see cref="CharacterVisual"/> from the hitboxes.
/// </summary>
public partial class CharacterModel : Node3D
{
    /// <summary>The bones a splat on each body part may stick to (the nearest one wins).</summary>
    private static readonly Dictionary<HitboxPart, string[]> SplatBones = new()
    {
        [HitboxPart.Torso] = new[] { "Spine02", "Spine01", "Spine" },
        [HitboxPart.Head] = new[] { "Head" },
        [HitboxPart.Mask] = new[] { "Head" },
        [HitboxPart.Arms] = new[] { "LeftArm", "LeftForeArm", "RightArm", "RightForeArm" },
        [HitboxPart.Legs] = new[] { "Hips", "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg" },
    };

    /// <summary>Each model's upper-arm sleeves where the bands go: the circle round them (its middle off the bone, in the bone's frame, and radius, in skeleton units), or null where unmeasured.</summary>
    private static readonly Dictionary<(string Path, string Bone), (Vector3 Middle, float Radius)?> Sleeves = new();

    private readonly Dictionary<string, BoneAttachment3D> _attachments = new();
    private Skeleton3D _skeleton = null!;
    private AnimationPlayer? _animation;

    public CharacterPoser Poser { get; private set; } = null!;

    /// <summary>How the legs move: the movement clips, or steps without them.</summary>
    public Gait Gait { get; private set; } = null!;

    /// <summary>The model at <paramref name="path"/>, or null when the file is missing or isn't the generator's rig.</summary>
    public static CharacterModel? TryCreate(string path, Color tint, Color team, CharactersDef def)
    {
        if (ArtFiles.Load<PackedScene>(path) is not { } scene)
        {
            return null;
        }

        Node3D instance = scene.Instantiate<Node3D>();
        Skeleton3D? skeleton = FindFirst<Skeleton3D>(instance);
        var poser = new CharacterPoser
        {
            Name = "Poser",
            ChestPitch = def.ChestPitch,
            HeadPitch = def.HeadPitch,
            Stride = def.Stride_m,
            StepLift = def.StepLift_m,
            HipBob = def.HipBob_m,
        };
        if (skeleton is null || !poser.Bind(skeleton))
        {
            GD.PushWarning($"{path} isn't a rig the character poser knows; drawing hitbox boxes instead");
            instance.QueueFree();
            poser.QueueFree();
            return null;
        }

        MoveClip? Clip(string name, string? file) => MoveClipBaker.Bake(name, file, path, instance, skeleton);
        var gait = new Gait(def, Clip("walk", def.Clips.Walk), Clip("run", def.Clips.Run), Clip("crouchWalk", def.Clips.CrouchWalk));
        poser.Gait = gait;
        var model = new CharacterModel { Name = "Model", _skeleton = skeleton, Poser = poser, Gait = gait };
        // Generated models face +Z; characters here face −Z.
        instance.RotationDegrees = new Vector3(0f, 180f, 0f);
        model.AddChild(instance);
        skeleton.AddChild(poser);
        model._animation = FindFirst<AnimationPlayer>(instance);
        model.Tint(instance, tint);
        float unit = SkeletonScale(instance, skeleton);
        model.AddArmband(path, instance, "LeftArm", "LeftForeArm", team, def, unit);
        model.AddArmband(path, instance, "RightArm", "RightForeArm", team, def, unit);
        foreach (string[] bones in SplatBones.Values)
        {
            foreach (string bone in bones)
            {
                model.Attachment(bone);
            }
        }

        return model;
    }

    public override void _Ready()
    {
        // The idle clip loops underneath the procedural pose.
        if (_animation is not null && _animation.GetAnimationList() is { Length: > 0 } clips)
        {
            Animation clip = _animation.GetAnimation(clips[0]);
            clip.LoopMode = Animation.LoopModeEnum.Linear;
            _animation.Play(clips[0]);
            _animation.Seek(GD.Randf() * (float)clip.Length, update: true);
        }
    }

    /// <summary>
    /// The bone a splat at <paramref name="point"/> on <paramref name="part"/> should stick to, or null for
    /// gear. The point is on the part's hitbox, a box round the body that the body doesn't fill (and
    /// bulges past in places): the paint reaches as far as the bone, in and out, to find the surface.
    /// </summary>
    public SplatAnchor? PartNode(HitboxPart part, Vector3 point)
    {
        if (!SplatBones.TryGetValue(part, out string[]? bones))
        {
            return null;
        }

        Node3D? best = null;
        float bestDistance = float.MaxValue;
        foreach (string bone in bones)
        {
            int index = _skeleton.FindBone(bone);
            int[] children = _skeleton.GetBoneChildren(index);
            Vector3 a = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(index).Origin;
            Vector3 b = children.Length > 0 ? _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(children[0]).Origin : a;
            float distance = point.DistanceTo(Geometry3D.GetClosestPointToSegment(point, a, b));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = _attachments[bone];
            }
        }

        return best is null ? null : new SplatAnchor(best, bestDistance);
    }

    private BoneAttachment3D Attachment(string bone)
    {
        if (!_attachments.TryGetValue(bone, out BoneAttachment3D? attachment))
        {
            attachment = new BoneAttachment3D { Name = "On" + bone, BoneName = bone };
            _skeleton.AddChild(attachment);
            _attachments[bone] = attachment;
        }

        return attachment;
    }

    /// <summary>How far down the upper arm the bands sit (0 at the shoulder, 1 at the elbow): clear of the shoulder, where the skin blends into the body.</summary>
    private const float Along = 0.5f;

    /// <summary>How many ways round the arm a band's shape is measured and drawn.</summary>
    private const int Sides = 16;

    /// <summary>
    /// A band in the team colour round the upper arm, halfway down: a ring round the model's sleeve there
    /// (<see cref="Sleeve"/>) with a little room, so it wraps a padded sleeve and a slim one alike, or as wide
    /// as the data says round the bone where the sleeve can't be measured.
    /// </summary>
    private void AddArmband(string path, Node3D instance, string bone, string child, Color team, CharactersDef def, float unit)
    {
        int index = _skeleton.FindBone(bone);
        int next = _skeleton.FindBone(child);
        if (index < 0 || next < 0)
        {
            return;
        }

        // Where the elbow is in the upper arm's own frame: the band lies along that line.
        Vector3 along = _skeleton.GetBoneGlobalRest(index).AffineInverse() * _skeleton.GetBoneGlobalRest(next).Origin;
        Basis frame = Conv.BasisFromUp(along.Normalized(), 0f);
        if (!Sleeves.TryGetValue((path, bone), out (Vector3 Middle, float Radius)? sleeve))
        {
            sleeve = Sleeve(instance, index, along, frame);
            Sleeves[(path, bone)] = sleeve;
            GD.Print(sleeve is { } fit
                ? $"Armband on {path.GetFile()} {bone}: sleeve {fit.Radius * unit * 100f:0.0} cm round, {fit.Middle.Length() * unit * 100f:0.0} cm off the bone"
                : $"Armband on {path.GetFile()} {bone}: sleeve not measured, {def.ArmbandRadius_m * 100f:0.0} cm round");
        }

        float radius = sleeve is { } s ? s.Radius + def.ArmbandGap_m / unit : def.ArmbandRadius_m / unit;
        var band = new MeshInstance3D
        {
            Name = "Armband",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = def.ArmbandWidth_m / unit,
                RadialSegments = Sides,
                Rings = 1,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = team, Roughness = 0.7f, EmissionEnabled = true, Emission = team, EmissionEnergyMultiplier = 0.25f,
            },
            Transform = new Transform3D(frame, along * Along + (sleeve?.Middle ?? Vector3.Zero)),
        };
        Attachment(bone).AddChild(band);
    }

    /// <summary>
    /// The model's sleeve round the bone <paramref name="bone"/> halfway along it (towards
    /// <paramref name="along"/>, in the bone's frame), in skeleton units: first how far out it reaches in
    /// each of <see cref="Sides"/> directions round the bone (<paramref name="frame"/>'s X towards Z), read from
    /// the vertices skinned mostly to that bone, in a slice a sixth of the bone long, put into the bone's frame
    /// by the skin's bind poses; ones much further out than most (the shoulder or the chest, skinned to the
    /// arm) are left out, and a direction with none takes its neighbours'. Then a circle is fitted round
    /// those reaches. Null when too few vertices are found.
    /// </summary>
    private (Vector3 Middle, float Radius)? Sleeve(Node3D instance, int bone, Vector3 along, Basis frame)
    {
        float length = along.Length();
        if (length < 1e-4f)
        {
            return null;
        }

        Vector3 axis = along / length;
        var across = new List<Vector3>();
        foreach (Node node in instance.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
        {
            if (node is not MeshInstance3D { Mesh: { } mesh, Skin: { } skin })
            {
                continue;
            }

            // The skin's bind that stands for this bone (by name, or by index when the binds aren't named).
            int bind = -1;
            for (int b = 0; b < skin.GetBindCount() && bind < 0; b++)
            {
                StringName name = skin.GetBindName(b);
                if (name.IsEmpty ? skin.GetBindBone(b) == bone : _skeleton.FindBone(name) == bone)
                {
                    bind = b;
                }
            }

            if (bind < 0)
            {
                continue;
            }

            Transform3D intoBone = skin.GetBindPose(bind);
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                float[] weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = vertices.Length > 0 ? bones.Length / vertices.Length : 0;
                if (per == 0 || weights.Length != bones.Length)
                {
                    continue;
                }

                for (int v = 0; v < vertices.Length; v++)
                {
                    float weight = 0f;
                    for (int k = 0; k < per; k++)
                    {
                        if (bones[v * per + k] == bind)
                        {
                            weight += weights[v * per + k];
                        }
                    }

                    if (weight < 0.5f)
                    {
                        continue;
                    }

                    Vector3 p = intoBone * vertices[v];
                    if (Math.Abs(p.Dot(axis) / length - Along) <= 0.08f)
                    {
                        across.Add(p - axis * p.Dot(axis));
                    }
                }
            }
        }

        if (across.Count < 12)
        {
            return null;
        }

        var out_ = new List<float>(across.Count);
        foreach (Vector3 q in across)
        {
            out_.Add(q.Length());
        }

        out_.Sort();
        float limit = out_[out_.Count / 2] * 1.5f;
        var sides = new List<float>[Sides];
        for (int k = 0; k < Sides; k++)
        {
            sides[k] = new List<float>();
        }

        foreach (Vector3 q in across)
        {
            float r = q.Length();
            if (r <= limit)
            {
                float angle = Mathf.Atan2(q.Dot(frame.Z), q.Dot(frame.X));
                sides[((int)Mathf.Round(angle / Mathf.Tau * Sides) % Sides + Sides) % Sides].Add(r);
            }
        }

        // Most of the way out in each direction, so a stray vertex or two doesn't push the band off the sleeve.
        var reach = new float[Sides];
        for (int k = 0; k < Sides; k++)
        {
            if (sides[k].Count > 0)
            {
                sides[k].Sort();
                reach[k] = sides[k][(int)((sides[k].Count - 1) * 0.85f)];
            }
        }

        // Directions the slice had nothing in take the nearest measured ones either side.
        var filled = (float[])reach.Clone();
        for (int k = 0; k < Sides; k++)
        {
            if (reach[k] > 0f)
            {
                continue;
            }

            float before = 0f, after = 0f;
            int db = 1, da = 1;
            while (db < Sides && reach[(k - db + Sides) % Sides] <= 0f)
            {
                db++;
            }

            while (da < Sides && reach[(k + da) % Sides] <= 0f)
            {
                da++;
            }

            before = reach[(k - db + Sides) % Sides];
            after = reach[(k + da) % Sides];
            filled[k] = before > 0f && after > 0f ? Mathf.Lerp(before, after, (float)db / (db + da)) : Math.Max(before, after);
        }

        // A circle round it: a sleeve off the bone reaches further one way than the other, by its offset
        // (the reaches' first harmonic); the radius then covers all but a stray direction.
        Vector3 middle = Vector3.Zero;
        for (int k = 0; k < Sides; k++)
        {
            float a = Mathf.Tau * k / Sides;
            middle += (frame.X * Mathf.Cos(a) + frame.Z * Mathf.Sin(a)) * filled[k] * (2f / Sides);
        }

        var radii = new float[Sides];
        for (int k = 0; k < Sides; k++)
        {
            float a = Mathf.Tau * k / Sides;
            radii[k] = filled[k] - middle.Dot(frame.X * Mathf.Cos(a) + frame.Z * Mathf.Sin(a));
        }

        Array.Sort(radii);
        return (middle, radii[(int)((Sides - 1) * 0.85f)]);
    }

    /// <summary>Tints every surface's colour, so copies of one model don't look like triplets.</summary>
    private void Tint(Node root, Color tint)
    {
        if (tint == Colors.White)
        {
            return;
        }

        foreach (Node node in root.FindChildren("*", nameof(MeshInstance3D), recursive: true, owned: false))
        {
            var mesh = (MeshInstance3D)node;
            for (int i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mesh.Mesh?.SurfaceGetMaterial(i) is BaseMaterial3D material)
                {
                    var tinted = (BaseMaterial3D)material.Duplicate();
                    tinted.AlbedoColor *= tint;
                    mesh.SetSurfaceOverrideMaterial(i, tinted);
                }
            }
        }
    }

    /// <summary>Metres per skeleton unit (the generator's rig is in centimetres under a 0.01 scale).</summary>
    private static float SkeletonScale(Node3D root, Skeleton3D skeleton)
    {
        Transform3D transform = skeleton.Transform;
        for (Node? node = skeleton.GetParent(); node is not null && node != root; node = node.GetParent())
        {
            if (node is Node3D spatial)
            {
                transform = spatial.Transform * transform;
            }
        }

        return transform.Basis.Scale.X;
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            if (FindFirst<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
