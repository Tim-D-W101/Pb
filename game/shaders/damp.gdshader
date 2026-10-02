// Damp indoors (Damp): cards flat on the walls and ceilings, blended over them and lit with them.
// Rising damp (band = true) repeats a painted band along the wall: instance custom data red = repeats
// across the card, green = where along the band it starts. Mould and water stains (band = false) pick a
// cell of a 2 x 2 atlas: red = cell, green = 1 to mirror it. Blue is the opacity either way; the
// instance colour shades it. They fade out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool band = false;
uniform float fade_start = 30.0;
uniform float fade_end = 50.0;

varying vec2 tex_uv;
varying float opacity;

void vertex() {
	vec4 c = INSTANCE_CUSTOM;
	if (band) {
		tex_uv = vec2(UV.x * c.r + c.g, UV.y);
	} else {
		vec2 uv = vec2(mix(UV.x, 1.0 - UV.x, c.g), UV.y);
		tex_uv = (vec2(mod(c.r, 2.0), floor(c.r / 2.0)) + uv) * 0.5;
	}
	opacity = c.b;
}

void fragment() {
	vec4 t = texture(tex, tex_uv);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = t.rgb * COLOR.rgb;
	ALPHA = t.a * opacity * fade;
	ROUGHNESS = 0.95;
	SPECULAR = 0.3;
}
