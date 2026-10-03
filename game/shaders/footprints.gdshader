// Footprints pressed into the ground (Footprints): a boot's sole on a flat card (the print painted at
// load: alpha = how deep it's pressed, the tread darker) darkening the ground under it by the instance
// colour (a multiply, so the soil's own grain shows through), as far as the colour's alpha says, and
// fading out over the time the print lasts (custom data: r = when it was made, on the `now` clock; g =
// how long it lasts; b = left (0) or right (1) foot, mirroring it).
shader_type spatial;
render_mode blend_mul, depth_draw_never, cull_disabled, unshaded;

uniform sampler2D sole : source_color, filter_linear_mipmap, repeat_disable;
uniform float now = 0.0;

varying float v_fade;
varying float v_right;

void vertex() {
	float age = now - INSTANCE_CUSTOM.r;
	float life = max(INSTANCE_CUSTOM.g, 0.01);
	v_fade = age < 0.0 || age > life ? 0.0 : 1.0 - smoothstep(life * 0.6, life, age);
	v_right = INSTANCE_CUSTOM.b;
	if (v_fade <= 0.0) {
		VERTEX *= 0.0;
	}
}

void fragment() {
	vec2 uv = vec2(v_right > 0.5 ? 1.0 - UV.x : UV.x, UV.y);
	vec4 s = texture(sole, uv);
	ALBEDO = mix(vec3(1.0), COLOR.rgb * s.rgb, s.a * COLOR.a * v_fade);
}
