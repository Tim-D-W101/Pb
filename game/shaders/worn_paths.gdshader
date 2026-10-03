// Paths worn across the soft ground (WornPaths): ribbons along each path, blended over the ground and lit
// with it. UV: metres along the path, and -1 to 1 across the ribbon (narrowing towards a path's ends).
// The trodden middle (width_share of the ribbon) is dustier and smoother, its edges ragged where feet
// strayed, patchy along its length; it fades out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec3 color : source_color = vec3(0.6, 0.54, 0.45);
uniform float opacity = 0.45;
uniform float width_share = 0.62;
uniform float fade_start = 40.0;
uniform float fade_end = 70.0;

varying vec2 world;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	float across = abs(UV.y);
	// A ragged edge, and the middle worn more in some stretches than others.
	float ragged = width_share * (0.75 + 0.5 * noise(world * 2.2) + 0.15 * noise(world * 9.0));
	float edge = 1.0 - smoothstep(ragged * 0.55, ragged, across);
	float patchy = 0.55 + 0.45 * noise(vec2(UV.x * 0.35, 3.1)) ;
	float grit = 0.85 + 0.15 * noise(world * 23.0);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = color * grit;
	ALPHA = opacity * edge * patchy * fade;
	ROUGHNESS = 0.95;
	SPECULAR = 0.3;
}
