// The small pools where water drips from a hole in the roof (RoofDrips): a dark, glossy, wavering round
// patch on a flat card, deepest in the middle, mirroring the light like the puddles.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec3 water : source_color = vec3(0.05, 0.05, 0.055);

varying float v_seed;

void vertex() {
	v_seed = INSTANCE_CUSTOM.r;
}

void fragment() {
	vec2 d = UV - 0.5;
	float angle = atan(d.y, d.x);
	float r = length(d) * 2.0 * (1.0 + 0.12 * sin(angle * 3.0 + v_seed) + 0.07 * sin(angle * 7.0 - v_seed * 1.3));
	float wet = 1.0 - smoothstep(0.55, 1.0, r);
	ALBEDO = water;
	ROUGHNESS = mix(0.4, 0.04, 1.0 - smoothstep(0.3, 0.8, r));
	SPECULAR = 0.6;
	ALPHA = wet * 0.85;
}
