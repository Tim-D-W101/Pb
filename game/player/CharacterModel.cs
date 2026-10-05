using System.Collections.Generic;
using Godot;
using Pb.Game.Ballistics;
using Pb.Game.Core;
using Pb.Sim.Collision;

namespace Pb.Game.Player;

/// <summary>
/// A rigged character model from the art pipeline standing in for the hitbox boxes. Its idle clip
/// loops, a <see cref="CharacterPoser"/> bends it to the sim's pose, armbands in the team colour sit on
/// both upper arms, and bone attachments give splats something to stick to. Gear (marker, loader,
/// tank) is still drawn by <see cref="CharacterVisual"/> from the hitboxes.
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

    private readonly Dictionary<string, BoneAttachment3D> _attachments = new();
    private Skeleton3D _skeleton = null!;
    private AnimationPlayer? _animation;

    public CharacterPoser Poser { get; private set; } = null!;

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

        var model = new CharacterModel { Name = "Model", _skeleton = skeleton, Poser = poser };
        // Generated models face +Z; characters here face −Z.
        instance.RotationDegrees = new Vector3(0f, 180f, 0f);
        model.AddChild(instance);
        skeleton.AddChild(poser);
        model._animation = FindFirst<AnimationPlayer>(instance);
        model.Tint(instance, tint);
        float unit = SkeletonScale(instance, skeleton);
        model.AddArmband("LeftArm", "LeftForeArm", team, def, unit);
        model.AddArmband("RightArm", "RightForeArm", team, def, unit);
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

    /// <summary>A band in the team colour round the upper arm, about a third of the way down.</summary>
    private void AddArmband(string bone, string child, Color team, CharactersDef def, float unit)
    {
        int index = _skeleton.FindBone(bone);
        int next = _skeleton.FindBone(child);
        if (index < 0 || next < 0)
        {
            return;
        }

        // Where the elbow is in the upper arm's own frame: the band lies along that line.
        Vector3 along = _skeleton.GetBoneGlobalRest(index).AffineInverse() * _skeleton.GetBoneGlobalRest(next).Origin;
        var band = new MeshInstance3D
        {
            Name = "Armband",
            Mesh = new CylinderMesh
            {
                TopRadius = def.ArmbandRadius_m / unit,
                BottomRadius = def.ArmbandRadius_m / unit,
                Height = def.ArmbandWidth_m / unit,
                RadialSegments = 16,
                Rings = 1,
            },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = team, Roughness = 0.7f, EmissionEnabled = true, Emission = team, EmissionEnergyMultiplier = 0.25f },
            Transform = new Transform3D(Conv.BasisFromUp(along.Normalized(), 0f), along * 0.35f),
        };
        Attachment(bone).AddChild(band);
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
