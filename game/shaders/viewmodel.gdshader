// First-person marker: drawn with its own vertical FOV (independent of the player's FOV setting)
// and with depth squashed into the nearest slice of the (reversed-Z) depth range, so it always
// draws in front of the world and never clips into a bunker the player is hugging.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(0.18, 0.18, 0.2, 1.0);
uniform float roughness : hint_range(0.0, 1.0) = 0.5;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
// Textures of the generated marker model (white when unused, so the plain values above apply). The
// roughness and metallic maps are read through a channel mask, as the model's own material says.
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, hint_default_white;
uniform sampler2D roughness_tex : filter_linear_mipmap_anisotropic, hint_default_white;
uniform vec4 roughness_channel = vec4(1.0, 0.0, 0.0, 0.0);
uniform sampler2D metallic_tex : filter_linear_mipmap_anisotropic, hint_default_white;
uniform vec4 metallic_channel = vec4(1.0, 0.0, 0.0, 0.0);
uniform bool use_normal_tex = false;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform float viewmodel_fov_deg = 62.0;
// Fraction of the depth range left to the world; the marker lives in the rest (nearest).
uniform float depth_squash : hint_range(0.5, 0.999) = 0.9;
// Paint on the marker (Phase 5, game/player/ViewModelPaint.cs): up to eight splats in the view model's own frame
// (vm_from_world takes a point there from the world). paint_at: centre and radius (0: none); paint_color: colour, and
// in alpha how far it has run down (0-1).
uniform mat4 vm_from_world;
uniform vec4 paint_at[8];
uniform vec4 paint_color[8];
varying vec3 vm_pos;

float paint_hash(vec3 p) {
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

float paint_noise(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(paint_hash(i), paint_hash(i + vec3(1, 0, 0)), u.x), mix(paint_hash(i + vec3(0, 1, 0)), paint_hash(i + vec3(1, 1, 0)), u.x), u.y);
	float b = mix(mix(paint_hash(i + vec3(0, 0, 1)), paint_hash(i + vec3(1, 0, 1)), u.x), mix(paint_hash(i + vec3(0, 1, 1)), paint_hash(i + vec3(1, 1, 1)), u.x), u.y);
	return mix(a, b, u.z);
}

void vertex() {
	mat4 projection = PROJECTION_MATRIX;
	float focal = 1.0 / tan(radians(viewmodel_fov_deg) * 0.5);
	float aspect = abs(projection[1][1] / projection[0][0]);
	projection[0][0] = sign(projection[0][0]) * focal / aspect;
	projection[1][1] = sign(projection[1][1]) * focal;
	POSITION = projection * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	// Reversed-Z: NDC depth 1 is the near plane. Remap [0, 1] → [depth_squash, 1].
	POSITION.z = POSITION.w * depth_squash + (1.0 - depth_squash) * POSITION.z;
	vm_pos = (vm_from_world * (MODEL_MATRIX * vec4(VERTEX, 1.0))).xyz;
}

void fragment() {
	ALBEDO = albedo.rgb * texture(albedo_tex, UV).rgb;
	ROUGHNESS = roughness * dot(texture(roughness_tex, UV), roughness_channel);
	METALLIC = metallic * dot(texture(metallic_tex, UV), metallic_channel);
	if (use_normal_tex) {
		NORMAL_MAP = texture(normal_tex, UV).rgb;
	}

	float cover = 0.0;
	vec3 colour = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		if (paint_at[i].w <= 0.0) {
			continue;
		}
		vec3 d = vm_pos - paint_at[i].xyz;
		// It runs down as it ages: below the middle the splat reaches further.
		float run = paint_color[i].a;
		if (d.y < 0.0) {
			d.y /= 1.0 + 3.0 * run;
			d.xz *= 1.0 + 1.5 * run * clamp(-d.y / paint_at[i].w, 0.0, 1.0);
		}
		float r = length(d) / paint_at[i].w;
		float ragged = paint_noise(vm_pos * 160.0 + float(i) * 7.13) - 0.5;
		float m = 1.0 - smoothstep(0.78, 1.0, r + 0.3 * ragged);
		if (m > cover) {
			cover = m;
			colour = paint_color[i].rgb;
		}
	}
	if (cover > 0.0) {
		// Wet paint: its colour, glossy, no metal.
		ALBEDO = mix(ALBEDO, colour, cover);
		ROUGHNESS = mix(ROUGHNESS, 0.22, cover);
		METALLIC = mix(METALLIC, 0.0, cover);
	}
}
