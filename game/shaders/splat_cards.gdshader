// The world's paint drawn as cards (game/ballistics/SplatCards.cs), when decals cost the PC too much: each card one cell
// of the wet splat atlas (game/ballistics/WetSplats.cs: red its coverage, green its thickness, blue the shell's flecks;
// instance custom data: red = cell, green = when it landed, s), coloured by the card (instance colour, linear; its alpha
// fades the oldest out). Fresh paint is wet, glossy and its colour deeper, and dulls to satin as it dries, as the
// decals do. They fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : filter_linear_mipmap_anisotropic, repeat_disable;
uniform float paint_now = 0.0;
uniform float drying_s = 40.0;
uniform float wet_roughness = 0.07;
uniform float dry_roughness = 0.55;
uniform float wet_darken = 0.12;
uniform float bump = 1.0;
uniform float fade_start = 70.0;
uniform float fade_end = 95.0;

varying vec2 atlas_uv;
varying float wet;

void vertex() {
	float cell = INSTANCE_CUSTOM.r;
	atlas_uv = (vec2(mod(cell, 4.0), floor(cell / 4.0)) + UV) * vec2(0.25, 0.5);
	wet = clamp(1.0 - (paint_now - INSTANCE_CUSTOM.g) / drying_s, 0.0, 1.0);
}

void fragment() {
	vec4 s = texture(atlas, atlas_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	float thick = clamp(s.g * 1.6, 0.0, 1.0);
	ALBEDO = COLOR.rgb * (1.0 - wet_darken * wet) * (0.9 - 0.12 * s.g + 0.22 * s.b);
	ALPHA = s.r * COLOR.a * fade;
	ROUGHNESS = mix(mix(0.42, dry_roughness, thick), mix(0.18, wet_roughness, thick), wet) + 0.12 * s.b;
	// Its thickness lifts it in the light: the height's slope on the screen, as paint.gdshaderinc does.
	float lift = s.g * 0.0006 * bump;
	vec3 vx = dFdx(VERTEX);
	vec3 vy = dFdy(VERTEX);
	vec3 r1 = cross(vy, NORMAL);
	vec3 r2 = cross(NORMAL, vx);
	float det = dot(vx, r1);
	vec3 bumped = abs(det) * NORMAL - sign(det) * (dFdx(lift) * r1 + dFdy(lift) * r2);
	if (dot(bumped, bumped) > 1e-30) {
		NORMAL = normalize(bumped);
	}
}
