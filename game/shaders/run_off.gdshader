// Run-off streaks (RunOff): cards on outside walls, each one cell of a painted atlas of streak
// patterns side by side, running down from the top (instance custom data: red = cell, green =
// opacity), coloured by the card (instance colour, linear: grime to rust). Matte, like the dirt they
// are. They fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float cells = 4.0;
uniform float fade_start = 40.0;
uniform float fade_end = 65.0;

varying vec2 atlas_uv;
varying float opacity;

void vertex() {
	atlas_uv = vec2((INSTANCE_CUSTOM.r + UV.x) / cells, UV.y);
	opacity = INSTANCE_CUSTOM.g;
}

void fragment() {
	vec4 c = texture(atlas, atlas_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a * opacity * fade;
	ROUGHNESS = 0.92;
	SPECULAR = 0.3;
}
