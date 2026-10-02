// Litter blowing about (BlowingLitter): flat cards cut from a 2 × 2 atlas painted at load (instance
// custom data r = the cell), tinted per piece (instance colour). Alpha-cut and two-sided, so a leaf
// tumbling over shows its underside lit too.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform vec2 grid = vec2(2.0, 2.0);

varying float cell;

void vertex() {
	cell = INSTANCE_CUSTOM.r;
}

void fragment() {
	vec2 at = vec2(mod(cell, grid.x), floor(cell / grid.x));
	vec4 c = texture(atlas, (at + clamp(UV, 0.01, 0.99)) / grid);
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.8;
	SPECULAR = 0.3;
	BACKLIGHT = COLOR.rgb * 0.25;
}
