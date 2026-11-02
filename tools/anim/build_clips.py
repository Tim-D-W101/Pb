"""
The characters' animation set (Phase 5, M5.2), keyframed by script in Blender 4.2 on the generator's biped rig.

    blender -b --factory-startup -P tools/anim/build_clips.py -- game/art/models/clip_walk.glb game/art/models/anim_set.glb [joints.json]

The first file gives the rig (any GLB on the generator's 24-bone biped: its armature is kept, its meshes and clips
dropped); the second is written: the rig with one action per clip, in place (the hips don't travel), at 30 frames a
second. The game bakes them onto each character's own build (game/player/MoveClipBaker.Set.cs) and blends them from the
body's movement (game/player/ClipBlender.cs). The step clips' ground speeds are the game's own data (presentation.jsonc
"characters" → "animPaces"), read from there, so the game picks each clip at the speed it was made for.

How the clips are made. Poses are written in the body's own terms, not the rig's: the hips' height, sway and turn; the
spine's bend and twist; and for the legs, where each ankle is and how the foot is pitched, solved to thigh and shin by
two-bone IK with the knee kept in the leg's plane. A step is a foot planted on the ground moving back under the body at
exactly the clip's ground speed (so a planted foot never slides when the clip plays at its speed), then a swing forward
on a minimum-jerk path, lifting, with the heel peeling off before and the toes up for the landing. Every cycle starts
with the left foot landing, so clips of one family blend in step. The arms are posed holding a marker at the chest; in
the game both hands are put on the marker by IK, so the arms' pose matters only where the hands let go (the refill's
support hand, hands up when hit).

Everything here is tunable: the gait table (GAITS), the clips (CLIPS) and the poses.
"""

import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30

# The rig is in centimetres with Z up and faces -Y; the character's right is -X.
UP = Vector((0.0, 0.0, 1.0))
FWD = Vector((0.0, -1.0, 0.0))
RIGHT = Vector((-1.0, 0.0, 0.0))


def deg(a):
    return math.radians(a)


def smooth(x):
    """Minimum-jerk ease from 0 to 1."""
    x = min(max(x, 0.0), 1.0)
    return x * x * x * (x * (x * 6.0 - 15.0) + 10.0)


def rot(axis, angle):
    return Quaternion(axis, angle)


# ---- The gait table -----------------------------------------------------------------------------------------------
# Per pace: cycles a second (a cycle is two steps), the share of the cycle each foot is in the air, how high it lifts
# (m), how far apart the feet run (m), the forward lean of the body (deg), how far the hips bob (m) and sink below
# standing (m), how far the arms' carry bounces (deg), and how much of its time on the ground a foot spends ahead of the
# body (landing) rather than behind it (pushing off: running, a foot comes down nearer under the body than it leaves). Each pace's ground speed (m/s) is the game's (data_paces): bots
# stroll at the walk's, walking (Alt) is the jog's, then running, sprinting and crouched.
GAITS = {
    "walk":   dict(cadence=1.05, swing=0.40, lift=0.09, width=0.17, lean=2.0, bob=0.022, sink=0.02, bounce=1.5, ahead=0.50),
    "jog":    dict(cadence=1.40, swing=0.58, lift=0.15, width=0.14, lean=5.0, bob=0.035, sink=0.04, bounce=2.5, ahead=0.45),
    "run":    dict(cadence=1.72, swing=0.68, lift=0.24, width=0.12, lean=8.0, bob=0.040, sink=0.06, bounce=3.5, ahead=0.40),
    "sprint": dict(cadence=1.95, swing=0.72, lift=0.30, width=0.11, lean=13.0, bob=0.045, sink=0.07, bounce=4.0, ahead=0.38),
    "crouch": dict(cadence=1.85, swing=0.42, lift=0.08, width=0.26, lean=12.0, bob=0.018, sink=0.0, bounce=1.5, ahead=0.50),
}

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "game", "data", "presentation.jsonc")


def data_paces(path=DATA):
    """The paces' ground speeds (m/s), from the game's data: presentation.jsonc "characters" → "animPaces"."""
    with open(path, encoding="utf-8") as f:
        text = f.read()
    # JSONC: comments out (not inside strings), then trailing commas.
    out, i, n, quoted = [], 0, len(text), False
    while i < n:
        c = text[i]
        if quoted:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1])
                i += 1
            elif c == '"':
                quoted = False
            i += 1
        elif c == '"':
            quoted = True
            out.append(c)
            i += 1
        elif text.startswith("//", i):
            while i < n and text[i] != "\n":
                i += 1
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
        else:
            out.append(c)
            i += 1
    data = json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))
    paces = data["characters"]["animPaces"]
    return {pace: float(paces[pace + "_mps"]) for pace in GAITS}


# The crouch: how far the hips sit below standing (m), the body's forward lean comes from the gait.
CROUCH_DROP = 0.36

# Directions, in the body's frame: degrees round from forward, to the right.
DIRECTIONS = {"f": 0.0, "r": 90.0, "b": 180.0, "l": 270.0}

# Running sideways the legs turn towards the travel (the chest stays on the aim): this share of the way, at most this far.
TRAVEL_TWIST = 0.8
MAX_TWIST = 55.0


class Rig:
    """The armature's rest pose, by joint: where each joint is and how each bone is turned, in armature space."""

    def __init__(self, arm):
        self.arm = arm
        self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.head = {b.name: b.head_local.copy() for b in self.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in self.bones}
        self.order = [b.name for b in self.bones]  # parents come before children in Blender's list
        h = self.head
        self.hips = h["Hips"].copy()
        self.thigh = {s: (h[s + "Leg"] - h[s + "UpLeg"]).length for s in ("Left", "Right")}
        self.shin = {s: (h[s + "Foot"] - h[s + "Leg"]).length for s in ("Left", "Right")}
        self.ankle_height = (h["LeftFoot"].z + h["RightFoot"].z) * 0.5
        self.toe_height = (h["LeftToeBase"].z + h["RightToeBase"].z) * 0.5
        self.ball_ahead = (h["LeftFoot"].y - h["LeftToeBase"].y)  # toe ahead of ankle (cm, along -Y)
        self.leg = self.thigh["Left"] + self.shin["Left"]


class Pose:
    """One frame: every bone's turn from rest in armature axes (total, parents included) and the hips' place."""

    def __init__(self, rig):
        self.rig = rig
        self.total = {n: Quaternion() for n in rig.order}
        self.delta = {n: Quaternion() for n in rig.order}
        self.hips = rig.hips.copy()
        self.fixed = {}  # bones whose total turn is set outright (IK), not built up from their parent

    def turn(self, bone, q):
        """Adds a turn to a bone (armature axes, as if at rest), on top of its parent's."""
        self.delta[bone] = q @ self.delta[bone]

    def solve(self):
        """Every bone's total turn, parents first: its parent's total times its own (unless IK set it)."""
        for n in self.rig.order:
            p = self.rig.parent[n]
            if n in self.fixed:
                self.total[n] = self.fixed[n]
            else:
                self.total[n] = (self.total[p] if p else Quaternion()) @ self.delta[n]
        return self

    def joint(self, bone):
        """Where a joint is in this pose (armature space): carried from its parent's joint by the parent's turn."""
        p = self.rig.parent[bone]
        if p is None:
            return self.hips.copy()
        offset = self.rig.head[bone] - self.rig.head[p]
        return self.joint(p) + self.total[p] @ offset


def frame_of(a, b):
    """An orthonormal frame (columns) from a main direction and a hint for its second axis."""
    a = a.normalized()
    c = a.cross(b).normalized()
    b2 = c.cross(a).normalized()
    return Matrix((a, b2, c)).transposed()


def leg_ik(pose, side, ankle, knee_hint, foot_pitch, foot_yaw):
    """
    Thigh and shin for the ankle at `ankle` (armature space), the knee bending towards `knee_hint`; the foot pitched by
    `foot_pitch` (rad, toes up positive) and turned `foot_yaw` about the vertical, from its rest.
    """
    rig = pose.rig
    up, low, foot = side + "UpLeg", side + "Leg", side + "Foot"
    pose.solve()
    hip = pose.joint(up)
    a, b = rig.thigh[side], rig.shin[side]
    to = ankle - hip
    d = min(to.length, (a + b) * 0.999)
    to = to.normalized() * d
    # The knee's place: in the plane through the hip and ankle square to the leg's own side-to-side axis, on its front
    # (so it never flips back, however far forward or back the foot is).
    cos_hip = (a * a + d * d - b * b) / (2 * a * d)
    ang = math.acos(max(-1.0, min(1.0, cos_hip)))
    ahead_flat = Vector((knee_hint.x, knee_hint.y, 0.0)).normalized()
    lateral = ahead_flat.cross(UP).normalized()  # the leg's own right
    side_dir = lateral.cross(to.normalized()).normalized()
    if side_dir.dot(ahead_flat) < 0.0:
        side_dir = -side_dir
    # A little out, as knees go.
    side_dir = (side_dir + (knee_hint - ahead_flat * knee_hint.dot(ahead_flat)) * 0.5).normalized()
    knee = hip + to.normalized() * (a * math.cos(ang)) + side_dir * (a * math.sin(ang))

    rest_thigh = rig.head[low] - rig.head[up]
    rest_shin = rig.head[foot] - rig.head[low]
    # The leg's own forward (where the knee points) maps from the rest pose's (forward), so the leg doesn't twist.
    ahead = Vector((knee_hint.x, knee_hint.y, 0.0)).normalized()
    q_thigh = (frame_of(knee - hip, ahead) @ frame_of(rest_thigh, FWD).transposed()).to_quaternion()
    q_shin = (frame_of(ankle - knee, ahead) @ frame_of(rest_shin, FWD).transposed()).to_quaternion()
    pose.fixed[up] = q_thigh
    pose.fixed[low] = q_shin
    # The foot: level as at rest, turned about the vertical, then pitched about its own right.
    q_yaw = rot(UP, foot_yaw)
    pose.fixed[foot] = rot(q_yaw @ RIGHT, foot_pitch) @ q_yaw


def arm_ik(pose, side, hand, elbow_hint):
    """Upper arm and forearm for the wrist at `hand` (armature space), the elbow bending towards `elbow_hint`."""
    rig = pose.rig
    up, low, end = side + "Arm", side + "ForeArm", side + "Hand"
    pose.solve()
    shoulder = pose.joint(up)
    a = (rig.head[low] - rig.head[up]).length
    b = (rig.head[end] - rig.head[low]).length
    to = hand - shoulder
    d = min(to.length, (a + b) * 0.999)
    to = to.normalized() * d
    cos_s = (a * a + d * d - b * b) / (2 * a * d)
    ang = math.acos(max(-1.0, min(1.0, cos_s)))
    side_dir = (elbow_hint - to.normalized() * elbow_hint.dot(to.normalized())).normalized()
    elbow = shoulder + to.normalized() * (a * math.cos(ang)) + side_dir * (a * math.sin(ang))
    back = -FWD
    q_up = (frame_of(elbow - shoulder, side_dir) @ frame_of(rig.head[low] - rig.head[up], back).transposed()).to_quaternion()
    q_low = (frame_of(shoulder + to - elbow, side_dir) @ frame_of(rig.head[end] - rig.head[low], back).transposed()).to_quaternion()
    pose.fixed[up] = q_up
    pose.fixed[low] = q_low


def chest_frame(pose):
    """Where the chest is and which way it faces (armature space): hands are placed from it."""
    pose.solve()
    top = pose.joint("Spine")
    turn = pose.total["Spine"]
    return top, turn


def carry_arms(pose, raise_deg=0.0):
    """
    The arms holding a marker at the shoulder, aimed, both hands on it (the game puts them there exactly by IK): the
    trigger (right) hand by the right of the chest, the support hand out along the barrel.
    """
    top, turn = chest_frame(pose)
    lift = UP * (raise_deg * 0.6)
    trigger = top + turn @ (RIGHT * 13.0 + FWD * 24.0 + UP * -14.0) + lift
    support = top + turn @ (RIGHT * 1.0 + FWD * 50.0 + UP * -12.0) + lift
    arm_ik(pose, "Right", trigger, turn @ (RIGHT * 0.6 + UP * -1.0 + FWD * -0.2))
    arm_ik(pose, "Left", support, turn @ (RIGHT * -0.7 + UP * -1.0))


def standing_base(pose, drop=0.0, lean=0.0, twist=0.0):
    """Hips down by `drop` (m), the body leaning forward `lean` degrees, the chest twisted back by `twist`."""
    pose.hips = pose.rig.hips + UP * (-drop * 100.0)
    pose.turn("Hips", rot(RIGHT, deg(-lean * 0.5)))
    for b, share in (("Spine02", 0.3), ("Spine01", 0.35), ("Spine", 0.35)):
        pose.turn(b, rot(RIGHT, deg(-lean * 0.5 * share)) @ rot(UP, deg(twist * share)))
    pose.turn("neck", rot(RIGHT, deg(lean * 0.35)))
    pose.turn("Head", rot(RIGHT, deg(lean * 0.35)))


def heel_off(s):
    """How far the heel is up (0-1) late in the stance, rolling over the ball of the foot."""
    return smooth((s - 0.62) / 0.38) if s > 0.62 else 0.0


def foot_path(q, g, stride, travel, rig, crouch, heading=None):
    """
    Where an ankle is (relative to the point under the hips, cm, as a vector) and the foot's pitch (rad, toes up
    positive), at `q` through its own cycle (0: landing). `travel` is the way the body goes; `heading` the way the foot
    points (the legs' heading; travel if not given). On the ground the foot is planted, moving back under the body at the
    ground speed; going the way it points, the heel then peels off and the foot rolls over the ball, which stays put while
    the ankle rises round it (sideways or backing off it stays flat). In the air it swings to where it lands on a Hermite
    curve: leaving at half the ground speed back under the body, landing at the whole of it (still in the world as it
    touches down), lifting quickly and coming down at the very end.
    """
    heading = travel if heading is None else heading
    stance = 1.0 - g["swing"]
    land = g["ahead"] * stance * stride     # the ankle lands this far ahead along the travel
    ball = rig.ball_ahead                   # the ball of the foot this far ahead of the ankle, flat
    drop = rig.ankle_height - rig.toe_height  # the ankle this far above the ball
    roll = max(0.0, travel.dot(heading))    # rolling over the ball only going the way the foot points
    peel = deg(36) * roll

    def planted(s):
        # The ball moves back under the body at ground speed through the whole stance; flat, the ankle is behind it along
        # the foot by `ball`; the heel coming up by theta turns that about the ball, the ankle rising and coming over it.
        ball_at = travel * land + heading * ball - travel * (s * stance * stride)
        theta = peel * heel_off(s)
        ankle = ball_at + heading * (-ball * math.cos(theta) + drop * math.sin(theta)) + UP * (ball * math.sin(theta) + drop * math.cos(theta) - drop)
        return ankle, -theta

    if q < stance:
        ankle, pitch = planted(q / stance)
        # Landing heel first, going forward: the toes come down from a little up.
        pitch += deg(6) * roll * (1.0 - smooth((q / stance) / 0.12))
        return ankle, pitch

    s = (q - stance) / (1.0 - stance)
    off, off_pitch = planted(1.0)
    landing = travel * land
    carried = (1.0 - stance) * stride
    m0, m1 = travel * (-0.5 * carried), travel * (-1.0 * carried)
    h00, h10 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s
    h01, h11 = -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
    flat = off * h00 + m0 * h10 + landing * h01 + m1 * h11
    flat.z = 0.0
    k = smooth(s)
    arc = g["lift"] * 100.0 * math.sin(math.pi * min(1.0, s)) ** 0.7 * (0.8 if crouch else 1.0)
    lift = off.z * (1.0 - k) + arc
    pitch = off_pitch * (1.0 - smooth(s / 0.35)) + deg(6) * roll * smooth((s - 0.65) / 0.35)
    return flat + UP * lift, pitch


def locomotion(rig, pace, direction, crouch=False):
    """The frames of one cycle of `pace` towards `direction` (degrees round from forward, to the right)."""
    g = GAITS[pace]
    frames = max(8, round(FPS / g["cadence"]))
    period = frames / FPS
    stride = g["speed"] * period * 100.0  # cm a cycle
    d = deg(direction)
    # Sideways the legs turn towards the travel; backing off they face forward.
    bearing = (direction + 180.0) % 360.0 - 180.0
    if abs(bearing) <= 120.0:
        twist = max(-MAX_TWIST, min(MAX_TWIST, TRAVEL_TWIST * bearing))
    else:
        twist = 0.0
    legs_yaw = deg(twist)
    travel = rot(UP, -d) @ FWD  # the travel in armature space (yaw to the right is negative about +Z)
    stance = 1.0 - g["swing"]

    def build(f, extra):
        p = (f % frames) / frames
        pose = Pose(rig)
        drop = (CROUCH_DROP if crouch else 0.0) + g["sink"] + extra
        bob = g["bob"] * (0.5 - 0.5 * math.cos(4.0 * math.pi * (p - 0.12)))  # lowest just after each landing
        standing_base(pose, drop=drop - bob, lean=g["lean"] + (8.0 if crouch else 0.0), twist=twist)
        # The hips turn towards the legs' heading, sway over the foot that's down and roll with the stride.
        sway = 0.025 * math.sin(2.0 * math.pi * p) * (0.4 if pace in ("run", "sprint") else 1.0)
        pose.hips = pose.hips + RIGHT * (-sway * 100.0)
        pose.turn("Hips", rot(UP, -legs_yaw + deg(5.0) * math.sin(2.0 * math.pi * p)) @ rot(FWD, deg(3.0) * math.sin(2.0 * math.pi * p)))
        carry_arms(pose, raise_deg=g["bounce"] * math.sin(4.0 * math.pi * p))
        pose.solve()
        # The feet from the point under the body at rest, not under the swaying hips: a planted foot stays put while the
        # hips sway over it.
        under = Vector((rig.hips.x, rig.hips.y, 0.0))
        need = 0.0
        for side, offset, sign in (("Left", 0.0, -1.0), ("Right", 0.5, 1.0)):
            q = (p + offset) % 1.0
            heading = rot(UP, -legs_yaw) @ FWD
            local, pitch = foot_path(q, g, stride, travel, rig, crouch, heading)
            local = local + rot(UP, -legs_yaw) @ (RIGHT * (sign * g["width"] * 0.5 * 100.0))
            ankle = under + local + UP * rig.ankle_height
            if q < stance or q > 1.0 - 0.3 * (1.0 - stance):
                # How much lower the hips must be for this planted (or landing) foot to be reached, the knee a little bent.
                hip = pose.joint(side + "UpLeg")
                reach = 0.97 * (rig.thigh[side] + rig.shin[side])
                flat = math.hypot(ankle.x - hip.x, ankle.y - hip.y)
                if flat < reach:
                    need = max(need, (hip.z - ankle.z) - math.sqrt(reach * reach - flat * flat))
            hint = rot(UP, -legs_yaw) @ (FWD + RIGHT * (0.1 * sign))
            leg_ik(pose, side, ankle, hint, pitch, -legs_yaw)
        return pose, max(0.0, need) / 100.0

    # First pass: how far down each frame needs the hips for its planted feet; smoothed round the cycle (a dip as a foot
    # lands far out ahead), then the frames again with it.
    needs = [build(f, 0.0)[1] for f in range(frames)]
    # Widened (each frame takes the most within a quarter of the cycle of it), then smoothed: a gentle dip, never short.
    wide = max(1, frames // 4)
    widened = [max(needs[(f + k) % frames] for k in range(-wide, wide + 1)) for f in range(frames)]
    smoothed = []
    for f in range(frames):
        total, weight = 0.0, 0.0
        for k in range(-wide, wide + 1):
            w = math.exp(-0.5 * (k / (0.6 * wide)) ** 2)
            total += widened[(f + k) % frames] * w
            weight += w
        smoothed.append(max(needs[f], total / weight))
    out = [build(f, smoothed[f % frames])[0] for f in range(frames + 1)]
    return out, period


def idle(rig, crouch=False, seconds=4.0):
    """Standing (or crouched) aiming: the weight shifting from foot to foot, breathing, the stance staggered."""
    frames = round(seconds * FPS)
    out = []
    for f in range(frames + 1):
        t = (f % frames) / frames
        pose = Pose(rig)
        shift = math.sin(2.0 * math.pi * t)
        breath = math.sin(2.0 * math.pi * t * 4.0)
        drop = (CROUCH_DROP + 0.03 if crouch else 0.025) + 0.006 * (1 - breath) * 0.5
        standing_base(pose, drop=drop, lean=(14.0 if crouch else 3.0))
        pose.hips = pose.hips + RIGHT * (0.025 * shift * 100.0)
        pose.turn("Hips", rot(FWD, deg(2.0) * shift) @ rot(UP, deg(-18)))  # bladed toward the trigger side
        pose.turn("Spine02", rot(UP, deg(6)))
        pose.turn("Spine01", rot(UP, deg(6)) @ rot(RIGHT, deg(0.6) * breath))
        pose.turn("Spine", rot(UP, deg(6)) @ rot(RIGHT, deg(0.6) * breath))
        carry_arms(pose, raise_deg=0.6 * breath)
        pose.solve()
        width = (0.4 if crouch else 0.3) * 100.0
        stagger = (0.3 if crouch else 0.22) * 100.0
        for side, sign in (("Left", -1.0), ("Right", 1.0)):
            ahead = stagger * (0.5 if side == "Left" else -0.5)
            ankle = Vector((rig.hips.x, rig.hips.y, 0.0)) + RIGHT * (sign * width * 0.5) + FWD * ahead + UP * rig.ankle_height
            if crouch and side == "Right":
                # Down on the trailing knee's side: that heel up.
                leg_ik(pose, side, ankle, FWD + RIGHT * (0.15 * sign), -deg(28), -deg(15) * sign)
            else:
                leg_ik(pose, side, ankle, FWD + RIGHT * (0.15 * sign), 0.0, -deg(12) * sign)
        out.append(pose)
    return out, seconds


def held(rig, build, seconds=1.0):
    """A pose held still (a short loop of one pose)."""
    frames = max(2, round(seconds * FPS))
    out = []
    for f in range(frames + 1):
        pose = Pose(rig)
        build(pose, f / frames)
        out.append(pose)
    return out, seconds


def stance_feet(pose, rig, crouch=False, lean_side=0.0):
    """Feet planted in the shooting stance."""
    pose.solve()
    width = (0.4 if crouch else 0.32) * 100.0
    for side, sign in (("Left", -1.0), ("Right", 1.0)):
        ahead = 11.0 if side == "Left" else -11.0
        ankle = Vector((rig.hips.x, rig.hips.y, 0.0)) + RIGHT * (sign * width * 0.5) + FWD * ahead + UP * rig.ankle_height
        leg_ik(pose, side, ankle, FWD + RIGHT * (0.15 * sign), 0.0, -deg(10) * sign)


def lean(rig, side):
    """Leaning out (the sim rolls the upper body; this is the legs and hips bracing): hips out the other way, the
    lean side's knee bent."""
    sign = -1.0 if side == "l" else 1.0

    def build(pose, _):
        standing_base(pose, drop=0.05, lean=4.0)
        pose.hips = pose.hips + RIGHT * (-sign * 0.06 * 100.0)
        pose.turn("Hips", rot(FWD, deg(4.0) * sign))
        carry_arms(pose)
        stance_feet(pose, rig)

    return held(rig, build)


def hands_up(rig):
    """Out: the marker up over the head in the trigger hand, the other hand up and open, standing tall."""

    def build(pose, t):
        standing_base(pose, drop=0.01, lean=-2.0)
        pose.turn("Hips", rot(FWD, deg(1.0) * math.sin(2.0 * math.pi * t)))
        pose.turn("neck", rot(RIGHT, deg(-4)))
        top, turn = chest_frame(pose)
        sway = UP * (1.0 * math.sin(2.0 * math.pi * t))
        # The marker up over the head in the trigger hand, the other hand up and open beside it.
        arm_ik(pose, "Right", top + turn @ (RIGHT * 16.0 + UP * 52.0 + FWD * 4.0) + sway, turn @ (RIGHT * 1.0 + FWD * -0.3))
        arm_ik(pose, "Left", top + turn @ (RIGHT * -20.0 + UP * 50.0 + FWD * 6.0) + sway, turn @ (RIGHT * -1.0 + FWD * -0.3))
        stance_feet(pose, rig)

    return held(rig, build, seconds=2.0)


def refill(rig, seconds=2.5):
    """The refill, start to end: the support hand drops to the pod on the belt, brings it up, tips it into the loader
    and goes back; the head bowed to watch, the body hunched a little."""
    frames = round(seconds * FPS)
    out = []
    for f in range(frames + 1):
        t = f / frames
        pose = Pose(rig)
        bow = smooth(t / 0.15) * (1.0 - smooth((t - 0.85) / 0.15))
        standing_base(pose, drop=0.04, lean=6.0 + 6.0 * bow)
        pose.turn("neck", rot(RIGHT, deg(12) * bow))
        pose.turn("Head", rot(RIGHT, deg(10) * bow))
        carry_arms(pose)
        reach = smooth((t - 0.05) / 0.2) * (1.0 - smooth((t - 0.3) / 0.15))      # down to the pouch
        lift = smooth((t - 0.3) / 0.15) * (1.0 - smooth((t - 0.8) / 0.15))       # up to the loader, pouring
        top, turn = chest_frame(pose)
        grip = top + turn @ (RIGHT * 1.0 + FWD * 50.0 + UP * -12.0)
        pouch = top + turn @ (RIGHT * -18.0 + FWD * -12.0 + UP * -55.0)
        loader = top + turn @ (RIGHT * 8.0 + FWD * 30.0 + UP * 6.0)
        hand = grip.lerp(pouch, reach) if lift <= 0.0 else pouch.lerp(loader, lift) if reach <= 0.0 else grip
        if t > 0.8:
            hand = loader.lerp(grip, smooth((t - 0.8) / 0.15))
        arm_ik(pose, "Left", hand, turn @ (RIGHT * -0.8 + UP * -1.0))
        tip = math.sin(math.pi * min(1.0, (t - 0.45) / 0.3)) if t > 0.45 else 0.0
        pose.turn("LeftHand", rot(FWD, deg(60) * lift * tip))
        stance_feet(pose, rig)
        out.append(pose)
    return out, seconds


def flinch(rig, seconds=0.4):
    """A ball's hit: the body jerks back from it, the head snaps, the knees give, and it comes back."""
    frames = round(seconds * FPS)
    out = []
    for f in range(frames + 1):
        t = f / frames
        k = smooth(t / 0.12) if t < 0.12 else 1.0 - smooth((t - 0.12) / 0.88)
        pose = Pose(rig)
        standing_base(pose, drop=0.03 + 0.04 * k, lean=-8.0 * k)
        pose.turn("Spine", rot(UP, deg(6) * k))
        pose.turn("neck", rot(RIGHT, deg(-10) * k))
        pose.turn("Head", rot(RIGHT, deg(-8) * k) @ rot(FWD, deg(6) * k))
        carry_arms(pose, raise_deg=6.0 * k)
        stance_feet(pose, rig)
        out.append(pose)
    return out, seconds


def clips(rig):
    """Every clip: name -> (frames, length in seconds, loops)."""
    made = {}
    made["idle"] = (*idle(rig), True)
    made["crouch_idle"] = (*idle(rig, crouch=True), True)
    for pace in ("walk", "jog", "run"):
        for key, d in DIRECTIONS.items():
            made[f"{pace}_{key}"] = (*locomotion(rig, pace, d), True)
    made["sprint_f"] = (*locomotion(rig, "sprint", 0.0), True)
    for key, d in DIRECTIONS.items():
        made[f"crouch_{key}"] = (*locomotion(rig, "crouch", d, crouch=True), True)
    made["lean_l"] = (*lean(rig, "l"), True)
    made["lean_r"] = (*lean(rig, "r"), True)
    made["refill"] = (*refill(rig), False)
    made["hit"] = (*flinch(rig), False)
    made["hands_up"] = (*hands_up(rig), True)
    return made


def joints(rig, frames):
    """Every frame's joint positions (m, Y up as in the game), for checking the clips without the engine."""
    out = []
    for pose in frames:
        pose.solve()
        out.append({n: [round(v / 100.0, 4) for v in (pose.joint(n).x, pose.joint(n).z, -pose.joint(n).y)] for n in rig.order})
    return out


def keyframe(arm, rig, name, frames):
    """One action from the frames: each bone's local turn (from its parent's pose) and the hips' place."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = action
    for f, pose in enumerate(frames):
        pose.solve()
        posed = {}
        for n in rig.order:
            p = rig.parent[n]
            rest = rig.rest[n]
            r = pose.total[n].to_matrix().to_4x4() @ Matrix.Translation(-rest.translation) @ rest
            r.translation = pose.joint(n)
            posed[n] = r
            parent_posed = posed[p] if p else Matrix.Identity(4)
            parent_rest = rig.rest[p] if p else Matrix.Identity(4)
            basis = (parent_posed @ parent_rest.inverted() @ rest).inverted() @ r
            pb = arm.pose.bones[n]
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = basis.to_quaternion()
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if n == "Hips":
                pb.location = basis.translation
                pb.keyframe_insert("location", frame=f)
    for fc in action.fcurves:
        for k in fc.keyframe_points:
            k.interpolation = 'LINEAR'
    track = arm.animation_data.nla_tracks.new()
    track.name = name
    # From frame 0, so the exported keys start at 0 s (Godot holds a clip at its first key until then: a frame's stall).
    track.strips.new(name, 0, action)
    arm.animation_data.action = None
    return action


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    source, target = argv[0], argv[1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    for o in list(bpy.data.objects):
        if o != arm and o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    if arm.animation_data:
        for t in list(arm.animation_data.nla_tracks):
            arm.animation_data.nla_tracks.remove(t)
    bpy.context.scene.render.fps = FPS
    for pace, speed in data_paces().items():
        GAITS[pace]["speed"] = speed
        print(f"PACE {pace}: {speed:.2f} m/s")
    rig = Rig(arm)
    made = clips(rig)
    dump = {}
    for name, (frames, length, loops) in made.items():
        keyframe(arm, rig, name, frames)
        dump[name] = {"length": length, "loops": loops, "frames": joints(rig, frames)}
        print(f"CLIP {name}: {len(frames) - 1} frames, {length:.2f} s, {'loop' if loops else 'once'}")
    if len(argv) > 2:
        import json
        with open(argv[2], "w") as f:
            json.dump(dump, f)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=target, export_format='GLB', use_selection=True, export_animations=True,
                              export_animation_mode='NLA_TRACKS', export_force_sampling=True, export_frame_step=1,
                              export_skins=True, export_morph=False, export_apply=False, export_yup=True)
    print(f"WROTE {target} with {len(made)} clips")


if __name__ == "__main__":
    main()
