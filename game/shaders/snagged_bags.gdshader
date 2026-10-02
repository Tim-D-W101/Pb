// Plastic bags caught on the barbed wire (SnaggedBags): a crumpled sheet hanging from the point it's
// caught on (the instance's origin; UV.y 0 at the top to 1 at the bottom), tinted per bag (instance
// colour; custom data r = the bag's phase). The wind holds it out downwind by an angle that gusts,
// ripples run down it and its free end twists, so no two bags move together. Two-sided; glossy and
// letting some light through, like thin plastic, unless told otherwise (the roof tatters).
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx, world_vertex_coords;

// Where the wind blows (world, horizontal, normalised), the angles it holds the bags out at (rad, in a
// lull and in a gust), how far ripples move the bottom (m) and how fast they run (rad/s).
uniform vec3 wind = vec3(1.0, 0.0, 0.0);
uniform float lean_least = 0.6;
uniform float lean_most = 1.0;
uniform float flap = 0.08;
uniform float flutter = 9.0;
// The surface: thin plastic is glossy and lets light through; roofing felt is neither.
uniform float roughness = 0.42;
uniform float specular = 0.45;
uniform float backlight = 0.45;

vec3 turn(vec3 v, vec3 axis, float angle) {
	return v * cos(angle) + cross(axis, v) * sin(angle) + axis * dot(axis, v) * (1.0 - cos(angle));
}

void vertex() {
	vec3 root = MODEL_MATRIX[3].xyz;
	float phase = INSTANCE_CUSTOM.r;
	float v = clamp(UV.y, 0.0, 1.0);
	// Gusts roll across the level; each bag also catches its own (0 a lull, 1 a gust).
	float gust = 0.5 + 0.3 * sin(TIME * 0.4 + dot(root.xz, wind.xz) * 0.05) + 0.2 * sin(TIME * 1.7 + phase);
	vec3 across = normalize(cross(wind, vec3(0.0, 1.0, 0.0)));
	float swing = mix(lean_least, lean_most, gust);
	float twist = 0.35 * v * sin(TIME * 0.9 + phase * 2.1);
	vec3 off = VERTEX - root;
	off = turn(off, vec3(0.0, 1.0, 0.0), twist);
	off = turn(off, across, swing);
	// Ripples running down the bag, stronger in a gust, flapping the free end most.
	float reach = pow(v, 1.5);
	off += wind * (flap * reach * sin(TIME * flutter + phase * 3.0 - v * 4.0) * (0.4 + gust));
	off += across * (0.5 * flap * reach * sin(TIME * flutter * 0.73 + phase - v * 3.0));
	VERTEX = root + off;
	NORMAL = turn(turn(NORMAL, vec3(0.0, 1.0, 0.0), twist), across, swing);
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = roughness;
	SPECULAR = specular;
	BACKLIGHT = COLOR.rgb * backlight;
}
