shader_type spatial;
render_mode blend_add, unshaded, cull_front, depth_draw_never, depth_test_disabled, fog_disabled, shadows_disabled;

// A sunbeam (LightShafts.cs). The mesh is a unit box whose node transform skews it into the beam: x
// across the opening, y up it (or across, for a roof hole), z along the light from the opening to
// where it lands. Each pixel of the box's back faces intersects its view ray with the box, cuts the
// segment off at whatever the depth buffer says is in front, and ray-marches the glow through it:
// soft edges that widen with distance, a fade along the beam, and slowly drifting dust swirls.

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 beam_color = vec3(1.0, 0.84, 0.68);
// Glow per metre of beam looked through, and its fade per metre from the opening.
uniform float density = 0.06;
uniform float falloff = 0.035;
// Soft edge at the opening (m) and how much it widens per metre.
uniform float softness = 0.1;
uniform float spread = 0.03;
uniform float patchiness = 0.55;
uniform float swirl_size = 1.4;
uniform float swirl_speed = 0.07;
// Glow nearer the camera than this fades out; beyond fade_start the whole beam fades with distance.
uniform float near_fade = 2.5;
uniform float fade_start = 45.0;
uniform float fade_end = 100.0;
uniform int samples = 6;

// Per beam: how much of the opening the sun reaches (times the roof-hole factor), and the beam's
// width, height and length in metres.
instance uniform float strength = 1.0;
instance uniform vec3 size_m = vec3(1.0, 1.0, 1.0);
// Per beam: the building it lights (world bounds). Where the light really hits a wall early, the box
// still runs on past it; this keeps that part from showing outside.
instance uniform vec3 clip_min = vec3(-1e6);
instance uniform vec3 clip_max = vec3(1e6);

varying vec3 v_local;
varying vec3 v_camera;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(hash13(i), hash13(i + vec3(1.0, 0.0, 0.0)), f.x);
	float b = mix(hash13(i + vec3(0.0, 1.0, 0.0)), hash13(i + vec3(1.0, 1.0, 0.0)), f.x);
	float c = mix(hash13(i + vec3(0.0, 0.0, 1.0)), hash13(i + vec3(1.0, 0.0, 1.0)), f.x);
	float d = mix(hash13(i + vec3(0.0, 1.0, 1.0)), hash13(i + vec3(1.0, 1.0, 1.0)), f.x);
	return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
}

void vertex() {
	v_local = VERTEX;
	v_camera = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
}

void fragment() {
	// The view ray in box space: t = 0 at the camera, t = 1 at this back-face pixel.
	vec3 ro = v_camera;
	vec3 rd = v_local - v_camera + vec3(1e-6);
	vec3 t0 = (vec3(-0.5) - ro) / rd;
	vec3 t1 = (vec3(0.5) - ro) / rd;
	vec3 tmin = min(t0, t1);
	vec3 tmax = max(t0, t1);
	float t_in = max(max(max(tmin.x, tmin.y), tmin.z), 0.0);
	float t_out = min(min(tmax.x, tmax.y), tmax.z);

	// The same ray in world space (an affine map keeps t), clipped to the building.
	vec3 wro = CAMERA_POSITION_WORLD;
	vec3 wrd = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz - wro + vec3(1e-6);
	vec3 c0 = (clip_min - wro) / wrd;
	vec3 c1 = (clip_max - wro) / wrd;
	vec3 cmin = min(c0, c1);
	vec3 cmax = max(c0, c1);
	t_in = max(t_in, max(max(cmin.x, cmin.y), cmin.z));
	t_out = min(t_out, min(min(cmax.x, cmax.y), cmax.z));

	// Whatever is drawn in front ends the beam: along one view ray, t scales with view depth.
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 scene = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	t_out = min(t_out, (scene.z / scene.w) / VERTEX.z);
	if (t_out <= t_in) {
		discard;
	}

	float ray_m = length(VERTEX);
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	vec3 drift = vec3(0.3, -1.0, 0.2) * (TIME * swirl_speed);
	float sum = 0.0;
	for (int i = 0; i < samples; i++) {
		float t = mix(t_in, t_out, (float(i) + jitter) / float(samples));
		vec3 q = ro + rd * t + 0.5;
		float along_m = q.z * size_m.z;
		vec2 edge_m = min(q.xy, 1.0 - q.xy) * size_m.xy;
		float soft = softness + spread * along_m;
		float glow = smoothstep(0.0, soft, edge_m.x) * smoothstep(0.0, soft, edge_m.y);
		glow *= smoothstep(0.0, 0.3, along_m) * (1.0 - smoothstep(0.8, 1.0, q.z)) * exp(-falloff * along_m);
		vec3 world = (MODEL_MATRIX * vec4(q - 0.5, 1.0)).xyz;
		glow *= mix(1.0, 1.7 * noise3((world + drift) / swirl_size), patchiness);
		sum += glow * smoothstep(0.2, near_fade, t * ray_m);
	}

	float optical = sum / float(samples) * (t_out - t_in) * ray_m * density * strength;
	float fade = 1.0 - smoothstep(fade_start, fade_end, ray_m * t_in);
	ALBEDO = beam_color * (1.0 - exp(-optical)) * fade;
	ALPHA = 1.0;
}
