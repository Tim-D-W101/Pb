// Paint trodden about (Footprints): a boot's sole on a flat card (the print painted at load: alpha = how
// much of it touches, the tread's bars darker in its colour channels), in the paint's colour (instance
// colour, sRGB; alpha = how much paint is left on the boot), the bars carrying more than the gaps, glossy
// like the splats, and fading out over the time the print lasts (custom data as footprints.gdshader).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

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
	float bars = 1.0 - smoothstep(0.7, 0.95, s.r);
	// The instance colour is sRGB, as the splats' is.
	ALBEDO = pow(COLOR.rgb, vec3(2.2));
	ROUGHNESS = 0.35;
	ALPHA = s.a * mix(0.45, 1.0, bars) * COLOR.a * v_fade;
}
