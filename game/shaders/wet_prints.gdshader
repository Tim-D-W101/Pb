// Wet footprints (Footprints): a boot that has stepped in a puddle leaves the print of its sole on hard
// ground, darkening it by the instance colour (a multiply) as far as the colour's alpha says. The tread's
// bars, which touch the ground, carry more water than the gaps between them. The print dries from its
// edges in, where it was pressed least, the bars last (custom data: r = when it was made, on the `now`
// clock; g = how long it takes to dry; b = left (0) or right (1) foot, mirroring it).
shader_type spatial;
render_mode blend_mul, depth_draw_never, cull_disabled, unshaded;

uniform sampler2D sole : source_color, filter_linear_mipmap, repeat_disable;
uniform float now = 0.0;

varying float v_dry;
varying float v_right;

void vertex() {
	float age = now - INSTANCE_CUSTOM.r;
	float life = max(INSTANCE_CUSTOM.g, 0.01);
	v_dry = age < 0.0 || age > life ? 1.0 : age / life;
	v_right = INSTANCE_CUSTOM.b;
	if (v_dry >= 1.0) {
		VERTEX *= 0.0;
	}
}

void fragment() {
	vec2 uv = vec2(v_right > 0.5 ? 1.0 - UV.x : UV.x, UV.y);
	vec4 s = texture(sole, uv);
	// The sole's painted shade is darker on the tread's bars (0.59) than between them (1).
	float bar = clamp((1.0 - s.r) / 0.41, 0.0, 1.0);
	float water = s.a * mix(0.6, 1.0, bar);
	// Drying: the wet area shrinks towards where the most water is.
	float wet = smoothstep(v_dry * 0.85, v_dry * 0.85 + 0.12, water);
	ALBEDO = mix(vec3(1.0), COLOR.rgb, wet * COLOR.a);
}
