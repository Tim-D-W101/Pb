shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

// Weeds and dry grass (WeedField.cs): crossed cards cut from a four-variant atlas, tinted per tuft
// (instance colour times the darker-at-the-base vertex colour), swaying in rolling gusts and shrinking
// into the ground with distance. They're lit like the ground under them (normal straight up), so no
// card ever shows a dark back face.

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float variants = 4.0;
// Tufts shrink between these distances from the camera (the graphics preset sets them).
uniform float fade_start = 34.0;
uniform float fade_end = 45.0;
// How far tips sway (m), how fast, the size of the gusts that roll across, and where the wind blows.
uniform float sway = 0.05;
uniform float wind_speed = 1.4;
uniform float gust_size = 12.0;
uniform vec2 wind_dir = vec2(0.82, 0.57);

varying float v_variant;

void vertex() {
	vec3 root = MODEL_MATRIX[3].xyz;
	float grow = 1.0 - smoothstep(fade_start, fade_end, distance(root, CAMERA_POSITION_WORLD));
	vec3 offset = VERTEX - root;
	float bend = pow(clamp(1.0 - UV.y, 0.0, 1.0), 1.5);
	float phase = dot(root.xz, vec2(0.37, 0.23));
	float gust = 0.55 + 0.45 * sin(TIME * 0.31 + dot(root.xz, wind_dir) / gust_size);
	float s = (sin(TIME * wind_speed + phase) * 0.65 + sin(TIME * wind_speed * 2.27 + phase * 1.9) * 0.25 + 0.35) * gust;
	offset.xz += wind_dir * (s * sway * bend);
	VERTEX = root + offset * grow;
	v_variant = floor(INSTANCE_CUSTOM.r + 0.5);
}

void fragment() {
	vec4 tex = texture(atlas, vec2((v_variant + clamp(UV.x, 0.01, 0.99)) / variants, UV.y));
	ALBEDO = tex.rgb * COLOR.rgb;
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.92;
	SPECULAR = 0.25;
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
}
