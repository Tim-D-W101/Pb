shader_type particles;
render_mode disable_velocity, disable_force;

// Dust motes in a sunbeam (LightShafts.cs): born anywhere in the beam, more of them near the opening,
// drifting slowly on a wandering path and fading in and out over their life.

// The beam: one corner, and its edges across, up (or across, for a roof hole) and along the light.
uniform vec3 origin = vec3(0.0);
uniform vec3 axis_a = vec3(1.0, 0.0, 0.0);
uniform vec3 axis_b = vec3(0.0, 1.0, 0.0);
uniform vec3 axis_c = vec3(0.0, 0.0, 1.0);
uniform float drift = 0.04;

float next(inout uint state) {
	state = state * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return float((word >> 22u) ^ word) / 4294967295.0;
}

void start() {
	uint state = NUMBER * 1973u + RANDOM_SEED * 9277u + 7u;
	vec3 r = vec3(next(state), next(state), next(state));
	float scale = mix(0.6, 1.4, next(state));
	TRANSFORM = mat4(vec4(scale, 0.0, 0.0, 0.0), vec4(0.0, scale, 0.0, 0.0), vec4(0.0, 0.0, scale, 0.0),
		vec4(origin + axis_a * r.x + axis_b * r.y + axis_c * pow(r.z, 1.5), 1.0));
	VELOCITY = (vec3(next(state), next(state), next(state)) - 0.5) * 2.0 * drift;
	CUSTOM = vec4(next(state) * 6.2832, 0.0, 0.0, 0.0);
	COLOR = vec4(1.0, 1.0, 1.0, 0.0);
}

void process() {
	CUSTOM.y += DELTA;
	float life = clamp(CUSTOM.y / LIFETIME, 0.0, 1.0);
	float phase = CUSTOM.x + CUSTOM.y * 0.7;
	vec3 wander = vec3(sin(phase), sin(phase * 1.3 + 1.7), cos(phase * 0.9)) * drift * 0.5;
	TRANSFORM[3].xyz += (VELOCITY + wander) * DELTA;
	COLOR.a = smoothstep(0.0, 0.2, life) * (1.0 - smoothstep(0.75, 1.0, life));
}
