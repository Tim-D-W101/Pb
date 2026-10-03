// The drifting cloud deck (CloudDeck): drawn on a dome round the camera, out near the far clip. Each
// view ray is cast up to a flat layer of cloud at `height` and reads two tiling noises there, moved by
// the wind, so the clouds keep their perspective and shrink towards the horizon. Thick cloud is grey
// underneath, its edges towards the sun bright, rims glowing near the sun; low down it thins into the
// haze. Unshaded and unfogged (the haze is done here), over the sky and under everything else.
shader_type spatial;
render_mode unshaded, fog_disabled, depth_draw_never, cull_disabled, blend_mix, shadows_disabled;

uniform sampler2D base_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_noise : filter_linear_mipmap, repeat_enable;
uniform float height = 1500.0;
uniform float tile = 3200.0;
uniform vec2 wind = vec2(5.0, -2.0);
uniform float cover = 0.75;
uniform float opacity = 0.92;
uniform vec3 sun_dir = vec3(0.0, 0.4, -0.9);
uniform vec3 lit_color : source_color = vec3(0.94, 0.9, 0.85);
uniform vec3 shade_color : source_color = vec3(0.52, 0.54, 0.58);
uniform vec3 haze_color : source_color = vec3(0.66, 0.65, 0.61);

float density(vec2 p) {
	vec2 uv = (p + wind * TIME) / tile;
	float n = texture(base_noise, uv).r * 0.8 + texture(detail_noise, uv * 2.3 + vec2(0.31, 0.77) + wind * TIME / tile * 0.6).r * 0.2;
	float threshold = mix(0.6, 0.34, cover);
	return smoothstep(threshold, threshold + 0.34, n);
}

void fragment() {
	vec3 cam = INV_VIEW_MATRIX[3].xyz;
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 dir = normalize(world - cam);
	if (dir.y < 0.005) {
		discard;
	}

	float t = (height - cam.y) / dir.y;
	vec2 p = cam.xz + dir.xz * t;
	float d = density(p);
	// Thinner a little way towards the sun: this side faces it.
	vec2 toward = normalize(sun_dir.xz + vec2(1e-4)) * tile * 0.012;
	float edge = clamp((d - density(p + toward)) * 2.5, 0.0, 1.0);
	float thick = smoothstep(0.15, 1.0, d);
	vec3 col = mix(lit_color, shade_color, thick * 0.8);
	col = mix(col, lit_color * 1.12, edge * 0.55);
	float near_sun = pow(max(dot(dir, normalize(sun_dir)), 0.0), 10.0);
	col += lit_color * near_sun * (1.0 - thick) * 0.7;
	// Into the haze low down, and gone at the horizon.
	float low = smoothstep(0.02, 0.3, dir.y);
	col = mix(haze_color, col, low);
	ALBEDO = col;
	ALPHA = d * opacity * smoothstep(0.01, 0.1, dir.y);
}
