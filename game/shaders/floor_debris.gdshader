// Debris on the floors indoors (FloorDebris): flat cards picking one cell of a painted atlas each
// (instance custom data: red = cell, green = opacity, blue = gloss), tinted by the card (instance
// colour, linear). Glass is glossy, the rest matte. Blended over the floor; they fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec2 grid = vec2(4.0, 3.0);
uniform float fade_start = 30.0;
uniform float fade_end = 50.0;

varying vec2 atlas_uv;
varying float opacity;
varying float gloss;

void vertex() {
	float cell = INSTANCE_CUSTOM.r;
	vec2 origin = vec2(mod(cell, grid.x), floor(cell / grid.x));
	atlas_uv = (origin + UV) / grid;
	opacity = INSTANCE_CUSTOM.g;
	gloss = INSTANCE_CUSTOM.b;
}

void fragment() {
	vec4 c = texture(atlas, atlas_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a * opacity * fade;
	ROUGHNESS = mix(0.88, 0.08, gloss);
	SPECULAR = mix(0.35, 0.9, gloss);
}
