// Ivy and dead vines on the walls (Creepers): tall cards against a wall, one of four painted variants
// each (instance custom data: red = variant), shaded per patch (instance colour), cut out at half
// coverage. They thin out with distance, then go.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float variants = 4.0;
uniform float fade_start = 45.0;
uniform float fade_end = 70.0;

varying float v_variant;

void vertex() {
	v_variant = floor(INSTANCE_CUSTOM.r + 0.5);
}

void fragment() {
	vec4 tex = texture(atlas, vec2((v_variant + clamp(UV.x, 0.01, 0.99)) / variants, UV.y));
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	ALBEDO = tex.rgb * COLOR.rgb;
	ALPHA = tex.a * fade;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.78;
	SPECULAR = 0.35;
}
