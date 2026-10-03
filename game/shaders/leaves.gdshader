// Leaf clumps on the trees beyond the level (Woods): crossed cards, each one cell of a painted atlas
// (instance custom data: xyz = the crown's outward normal here, w = cell), tinted per clump (instance
// colour). Alpha-cut, two-sided, lit as if the crown were round so the clumps shade together, light
// coming through from behind, and swaying in a slow gusting wind.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, world_vertex_coords;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform float cells = 4.0;
uniform float sway = 0.12;

varying vec3 crown_normal;
varying float cell;

void vertex() {
	crown_normal = INSTANCE_CUSTOM.xyz;
	cell = INSTANCE_CUSTOM.w;
	vec3 origin = MODEL_MATRIX[3].xyz;
	float phase = dot(origin.xz, vec2(0.13, 0.09));
	float gust = 0.55 + 0.45 * sin(TIME * 0.45 + origin.x * 0.02 + origin.z * 0.015);
	vec3 wave = vec3(sin(TIME * 1.6 + phase), 0.35 * sin(TIME * 2.3 + phase * 1.7), cos(TIME * 1.3 + phase * 1.3));
	// The clump's top moves more than its foot, as if hanging from its twigs.
	VERTEX += wave * sway * gust * (0.6 + 0.4 * (1.0 - UV.y));
}

void fragment() {
	vec4 c = texture(atlas, vec2((cell + UV.x) / cells, UV.y));
	ALBEDO = c.rgb * COLOR.rgb;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.45;
	NORMAL = normalize((VIEW_MATRIX * vec4(crown_normal, 0.0)).xyz);
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
	BACKLIGHT = COLOR.rgb * 0.4;
}
