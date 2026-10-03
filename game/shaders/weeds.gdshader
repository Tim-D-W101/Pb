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
// People pushing through (WeedField.Follow): each one's feet (xyz) and how hard they push (w, 1 where
// they are now, less where they were a moment ago, so the weeds spring back behind them), how far the
// push reaches, and how far it leans a tip over.
uniform vec4 pushers[32];
uniform int pusher_count = 0;
uniform float push_reach = 0.7;
uniform float push_lean = 0.28;

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
	// Leaning away from anyone close, and lying a little flatter, more the closer they are.
	vec2 shove = vec2(0.0);
	float flatten = 0.0;
	for (int i = 0; i < pusher_count; i++) {
		vec4 p = pushers[i];
		vec2 away = root.xz - p.xz;
		float d = length(away);
		if (d < push_reach && abs(root.y - p.y) < 0.8) {
			float k = 1.0 - d / push_reach;
			k *= k * p.w;
			shove += (d > 0.001 ? away / d : vec2(0.7071)) * k;
			flatten = max(flatten, k);
		}
	}
	float strength = length(shove);
	shove *= strength > 1.0 ? 1.0 / strength : 1.0;
	offset.xz += shove * push_lean * bend;
	offset.y *= 1.0 - 0.45 * flatten * bend;
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
