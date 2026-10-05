// Old paint from past games (OldPaint): cards on walls, cover and the ground, each one cell of a
// painted atlas of splat shapes (instance custom data: red = cell, green = opacity), coloured by the
// card (instance colour, linear). Dried paint is a dull semi-gloss. They fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float columns = 4.0;
uniform float fade_start = 40.0;
uniform float fade_end = 65.0;

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
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a * opacity * fade;
	ROUGHNESS = 0.62;
	SPECULAR = 0.42;
}
