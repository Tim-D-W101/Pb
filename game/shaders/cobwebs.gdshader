// Cobwebs in the corners of doorways and windows (Cobwebs): cards cut from a painted atlas of webs
// side by side (instance custom data: red = which web, green = opacity), seen from either side, a
// little sheen on the threads. They fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : hint_default_black, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float variants = 4.0;
uniform vec3 color : source_color = vec3(0.85);
uniform float fade_start = 20.0;
uniform float fade_end = 35.0;

varying vec2 atlas_uv;
varying float opacity;

void vertex() {
	atlas_uv = vec2((INSTANCE_CUSTOM.r + UV.x) / variants, UV.y);
	opacity = INSTANCE_CUSTOM.g;
}

void fragment() {
	vec4 c = texture(atlas, atlas_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = color;
	ALPHA = c.a * opacity * fade;
	ROUGHNESS = 0.55;
	SPECULAR = 0.6;
}
