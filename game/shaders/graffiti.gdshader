// Graffiti on the outside walls (Graffiti): cards flat on a wall, each reading one cell of the painted
// atlas (instance custom data: red = cell, green = opacity, blue = how worn it is, 0-1). The paint is
// worn away in patches and flecks (noise across the wall, so no two pieces wear alike), bleached
// towards grey by the sun, lit with the wall, and faded out with distance.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec2 grid = vec2(4.0, 4.0);
uniform float bleach = 0.3;
uniform float fade_start = 35.0;
uniform float fade_end = 60.0;

varying vec2 atlas_uv;
varying vec2 wall;
varying vec2 paint;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	float cell = INSTANCE_CUSTOM.r;
	vec2 origin = vec2(mod(cell, grid.x), floor(cell / grid.x));
	atlas_uv = (origin + UV) / grid;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Across and up the wall, whichever way it faces.
	wall = vec2(world.x + world.z, world.y);
	paint = INSTANCE_CUSTOM.gb;
}

void fragment() {
	vec4 c = texture(atlas, atlas_uv);
	float n = noise(wall * 2.3) * 0.55 + noise(wall * 9.0) * 0.3 + noise(wall * 37.0) * 0.15;
	float worn = smoothstep(1.0 - paint.y - 0.08, 1.0 - paint.y + 0.08, n);
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	vec3 grey = vec3(dot(c.rgb, vec3(0.3, 0.59, 0.11)));
	ALBEDO = mix(c.rgb, grey, bleach) * COLOR.rgb;
	ALPHA = c.a * paint.x * fade * (1.0 - worn);
	ROUGHNESS = 0.85;
	SPECULAR = 0.35;
}
