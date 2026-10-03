// Things hanging on the walls indoors (WallHangings): cards showing one rectangle of a painted atlas
// each (instance custom data: the rectangle's UVs, u0 v0 u1 v1), shaded a little per card (instance
// colour). Years of dust: the colours faded and yellowed, blotchy, darker towards the edges.
// Alpha-cut (the clock is round), lit like the wall, matte.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;

varying vec4 rect;

float hash(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	rect = INSTANCE_CUSTOM;
}

void fragment() {
	vec4 c = texture(atlas, mix(rect.xy, rect.zw, UV));
	vec3 col = c.rgb * COLOR.rgb;
	float grey = dot(col, vec3(0.3, 0.59, 0.11));
	col = mix(col, vec3(grey), 0.25) * vec3(1.0, 0.96, 0.88);
	// Blotches of dust and damp, different on every card.
	vec2 seed = rect.xy * 97.0 + COLOR.r * 13.0;
	float dirt = noise(UV * vec2(7.0, 5.0) + seed) * 0.65 + noise(UV * vec2(19.0, 15.0) + seed * 1.7) * 0.35;
	col *= mix(0.72, 1.0, smoothstep(0.2, 0.8, dirt));
	vec2 e = min(UV, 1.0 - UV);
	col *= mix(0.8, 1.0, smoothstep(0.0, 0.1, min(e.x, e.y)));
	ALBEDO = col;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.75;
	SPECULAR = 0.3;
}
