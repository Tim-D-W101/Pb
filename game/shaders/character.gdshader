// A character model in a player's kit (game/player/CharacterModel.cs, ClothesZones.cs): the model's own picture, normal,
// roughness and metal maps as its material had them, with the jersey and the pants repainted in the kit's colours. Each
// vertex carries its zone (COLOR: pants, jersey, head, face), its rest position (CUSTOM0, metres in the model's frame:
// +X its left, +Y up from its feet, +Z its front) and its rest normal (CUSTOM1), and the paint that's hit the body is
// kept in that frame (paint.gdshaderinc), so it moves with it. Recolouring keeps the picture's shading relative to the zone's mean
// brightness, so folds, seams and pockets stay; a pattern (bands, panels, camouflage, patches, a stripe) is laid out
// from the rest pose and the body's joints, so it moves with the body and never swims. Under a brand's mask the face is
// cut away, so the model's own mask never shows through.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, hint_default_white;
uniform vec4 albedo : source_color = vec4(1.0);
uniform bool use_normal_tex = false;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform float normal_scale = 1.0;
uniform sampler2D roughness_tex : hint_default_white, filter_linear_mipmap_anisotropic;
uniform vec4 roughness_channel = vec4(1.0, 0.0, 0.0, 0.0);
uniform float roughness : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D metallic_tex : hint_default_white, filter_linear_mipmap_anisotropic;
uniform vec4 metallic_channel = vec4(1.0, 0.0, 0.0, 0.0);
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
// Copies of one model differ by a tint.
uniform vec3 tint : source_color = vec3(1.0);

// The jersey and the pants: whether each is repainted, its colours (linear), its pattern (ClothesPatterns: 0 own,
// 1 yoke, 2 panels, 3 camo, 4 patches, 5 stripe) and the picture's mean brightness over it.
uniform bool jersey_on = false;
uniform vec3 jersey_main = vec3(1.0);
uniform vec3 jersey_second = vec3(1.0);
uniform vec3 jersey_accent = vec3(1.0);
uniform int jersey_pattern = 0;
uniform float jersey_mean = 0.05;
uniform bool pants_on = false;
uniform vec3 pants_main = vec3(1.0);
uniform vec3 pants_second = vec3(1.0);
uniform vec3 pants_accent = vec3(1.0);
uniform int pants_pattern = 0;
uniform float pants_mean = 0.05;
// How strongly the picture's shading shows through the new colour (1 as it is; less evens it out).
uniform float shading = 0.75;
// The body's joints in the rest pose, left side (the patterns mirror across): shoulder, elbow, wrist; hip, knee,
// ankle; the neck's, chest's, waist's and hips' heights.
uniform vec3 shoulder = vec3(0.17, 1.42, 0.0);
uniform vec3 elbow = vec3(0.29, 1.19, -0.06);
uniform vec3 wrist = vec3(0.42, 0.99, 0.0);
uniform vec3 hip = vec3(0.12, 0.87, -0.015);
uniform vec3 knee = vec3(0.17, 0.51, 0.02);
uniform vec3 ankle = vec3(0.24, 0.12, -0.07);
uniform vec4 heights = vec4(1.51, 1.4, 1.1, 0.96);
// A brand's mask is worn: the model's own face is cut away under it.
uniform bool hide_face = false;
// Checking the zones (--gear-zones): pants red, jersey green, head blue, the face white, the rest grey.
uniform bool show_zones = false;

#include "res://shaders/paint.gdshaderinc"

varying vec4 zone;
varying vec3 rest;
varying vec3 rest_normal;

void vertex() {
	zone = COLOR;
	rest = CUSTOM0.xyz;
	rest_normal = CUSTOM1.xyz;
}

float along(vec3 p, vec3 a, vec3 b) {
	vec3 ab = b - a;
	return clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.71, 0.113));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	return noise(p) * 0.6 + noise(p * 2.13 + 7.1) * 0.3 + noise(p * 4.7 + 3.3) * 0.1;
}

// Which colour a point of the jersey takes: 0 main, 1 second, 2 accent. q is the rest position folded onto the left.
int jersey_part(int pattern, vec3 q) {
	bool arm = q.x > shoulder.x - 0.02 && q.y < shoulder.y + 0.06;
	float t = along(q, shoulder, wrist);
	// Up the arm's top: away from the body, square to the arm in its plane.
	vec2 axis = normalize(wrist.xy - shoulder.xy);
	float over = dot(q.xy - shoulder.xy, vec2(-axis.y, axis.x));
	if (pattern == 1) {
		// A yoke across the shoulders in the second colour, its seam and the cuffs in the accent, a placket down the front.
		float yoke = mix(heights.y, heights.x, 0.3);
		if (arm) {
			return t > 0.9 ? 2 : (t < 0.2 && over > -0.012 ? 1 : 0);
		}
		if (q.y > yoke + 0.014) {
			return 1;
		}
		if (q.y > yoke) {
			return 2;
		}
		return q.x < 0.007 && q.z > 0.0 ? 2 : 0;
	} else if (pattern == 2) {
		// Side panels and the sleeves' undersides in the second colour, piped in the accent, with the cuffs and collar.
		if (arm) {
			if (t > 0.88) {
				return 2;
			}
			return over < -0.008 ? 1 : (over < 0.0 ? 2 : 0);
		}
		if (q.y > heights.x - 0.035) {
			return 2;
		}
		vec2 around = normalize(vec2(q.x, q.z - hip.z) + vec2(1e-5));
		return around.x > 0.8 ? 1 : (around.x > 0.74 ? 2 : 0);
	} else if (pattern == 3) {
		return fbm(q * 14.0 + 31.0) > 0.66 ? 2 : (fbm(q * 7.5) > 0.52 ? 1 : 0);
	}
	return 0;
}

// The pants' parts: 0 main, 1 second, 2 accent.
int pants_part(int pattern, vec3 q) {
	// The leg's middle at this height (ankle to knee to hip), and which way round the leg the point is.
	vec3 middle = q.y < knee.y
		? mix(ankle, knee, clamp((q.y - ankle.y) / max(knee.y - ankle.y, 0.05), 0.0, 1.0))
		: mix(knee, hip, clamp((q.y - knee.y) / max(hip.y - knee.y, 0.05), 0.0, 1.0));
	vec2 around = normalize(vec2(q.x - middle.x, q.z - middle.z) + vec2(1e-5));
	// The waistband: a band at the top of the pants (above it, the hem of what's worn over them takes the main colour).
	bool waistband = q.y > heights.w + 0.03 && q.y < heights.w + 0.07;
	if (pattern == 3) {
		return fbm(q * 14.0 + 31.0) > 0.66 ? 2 : (fbm(q * 7.5) > 0.52 ? 1 : 0);
	} else if (pattern == 4) {
		// Patches over the knees, stitched round in the accent, a pocket on each thigh, and the waistband.
		if (waistband) {
			return 2;
		}
		float k = abs(q.y - knee.y);
		if (k < 0.085 && around.y > 0.2) {
			return k > 0.077 || around.y < 0.26 ? 2 : 1;
		}
		float pocket = q.y - (hip.y - 0.36);
		if (pocket > 0.0 && pocket < 0.2 && around.x > 0.55) {
			return pocket > 0.185 || around.x < 0.6 ? 2 : 1;
		}
		return 0;
	} else if (pattern == 5) {
		// A stripe down the outside of each leg, piped in the accent, and the waistband.
		if (waistband) {
			return 2;
		}
		return around.x > 0.84 ? 1 : (around.x > 0.77 ? 2 : 0);
	}
	return 0;
}

vec3 pick(int part, vec3 main, vec3 second, vec3 accent) {
	return part == 0 ? main : (part == 1 ? second : accent);
}

void fragment() {
	if (hide_face && zone.a > 0.5) {
		discard;
	}

	vec3 picture = texture(albedo_tex, UV).rgb * albedo.rgb * tint;
	float bright = dot(picture, vec3(0.2126, 0.7152, 0.0722));
	vec3 q = vec3(abs(rest.x), rest.y, rest.z);
	vec3 colour = picture;
	if (jersey_on && zone.g > 0.0) {
		float shade = clamp(pow(bright / jersey_mean, shading), 0.2, 2.4);
		vec3 dressed = pick(jersey_part(jersey_pattern, q), jersey_main, jersey_second, jersey_accent) * shade;
		colour = mix(colour, dressed, zone.g);
	}
	if (pants_on && zone.r > 0.0) {
		float shade = clamp(pow(bright / pants_mean, shading), 0.2, 2.4);
		vec3 dressed = pick(pants_part(pants_pattern, q), pants_main, pants_second, pants_accent) * shade;
		colour = mix(colour, dressed, zone.r);
	}

	if (show_zones) {
		colour = zone.a > 0.5 ? vec3(1.0) : vec3(zone.r, zone.g, zone.b) * 0.8 + (1.0 - zone.r - zone.g - zone.b) * vec3(0.25);
	}

	float rough = roughness * dot(texture(roughness_tex, UV), roughness_channel);
	vec3 normal = NORMAL;
	float painted = splat_apply(rest, rest_normal, VERTEX, colour, rough, normal);
	NORMAL = normal;
	ALBEDO = colour;
	ROUGHNESS = rough;
	METALLIC = metallic * dot(texture(metallic_tex, UV), metallic_channel) * (1.0 - painted);
	if (use_normal_tex) {
		// Paint covers the cloth's weave.
		NORMAL_MAP = texture(normal_tex, UV).rgb;
		NORMAL_MAP_DEPTH = normal_scale * (1.0 - painted);
	}
}
