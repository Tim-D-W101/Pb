// Things lying on the ground (GroundDetail): flat cards picking one cell of a painted atlas each
// (instance custom data: red = cell, green = opacity), with a surface map for roughness and wetness
// so oil and puddles shine. Blended over the ground; they fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
// Red: roughness; green: wetness.
uniform sampler2D surface : hint_default_white, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float columns = 4.0;
uniform float fade_start = 45.0;
uniform float fade_end = 70.0;

varying vec2 atlas_uv;
varying float opacity;

void vertex() {
	float cell = INSTANCE_CUSTOM.r;
	vec2 origin = vec2(mod(cell, columns), floor(cell / columns));
	atlas_uv = (origin + UV) / columns;
	opacity = INSTANCE_CUSTOM.g;
}

void fragment() {
	vec4 c = texture(atlas, atlas_uv);
	vec4 s = texture(surface, atlas_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a * opacity * fade;
	ROUGHNESS = s.r;
	SPECULAR = mix(0.5, 0.65, s.g);
}
