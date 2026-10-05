// Dust kicked up underfoot (FootDust): soft puffs facing the camera, each born at the instance's origin
// (custom data: r = when it was kicked up, on the `now` clock; g = its size; b = how long it lasts;
// a = a random 0-1), in the colour of the ground (instance colour; alpha = how thick it starts). A puff
// billows out fast and slows, grows, rises a little and drifts on the wind, thinning to nothing. Lit
// like the ground under it, so it darkens in shade.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert_wrap, specular_disabled, world_vertex_coords;

uniform sampler2D puff : source_color, filter_linear_mipmap, repeat_disable;
// Seconds on the clock the puffs were stamped with.
uniform float now = 0.0;
// The wind carrying the dust (m/s, world), how fast it rises (m/s), and how much a puff grows.
uniform vec3 drift = vec3(0.6, 0.0, 0.1);
uniform float rise = 0.25;
uniform float grow = 2.2;

varying float v_alpha;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float age = now - INSTANCE_CUSTOM.r;
	float life = max(INSTANCE_CUSTOM.b, 0.01);
	float t = clamp(age / life, 0.0, 1.0);
	float out_t = 1.0 - (1.0 - t) * (1.0 - t);
	float size = INSTANCE_CUSTOM.g * mix(0.5, grow, sqrt(t));
	// Each puff leans off its own way a little as it goes.
	float a = INSTANCE_CUSTOM.a * 6.2832;
	vec3 wander = vec3(cos(a), 0.0, sin(a)) * 0.25 * INSTANCE_CUSTOM.g * out_t;
	vec3 centre = origin + drift * age + wander + vec3(0.0, rise * life * out_t + size * 0.3, 0.0);
	vec2 corner = (UV - 0.5) * vec2(1.0, -1.0);
	bool alive = age >= 0.0 && age <= life;
	// Dead puffs collapse to a point, so they cost nothing to draw.
	float s = alive ? size : 0.0;
	VERTEX = centre + (INV_VIEW_MATRIX[0].xyz * corner.x + INV_VIEW_MATRIX[1].xyz * corner.y) * s;
	NORMAL = vec3(0.0, 1.0, 0.0);
	v_alpha = alive ? (1.0 - t) * (1.0 - t) * smoothstep(0.0, 0.06, age) : 0.0;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ALPHA = texture(puff, UV).a * COLOR.a * v_alpha;
}
