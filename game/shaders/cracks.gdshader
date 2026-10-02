// Cracks in the asphalt and concrete (Cracks): flat ribbons along the crack lines, multiplied over the
// ground and the paint on it. UV: metres along the line, and -1 to 1 across the ribbon; UV2: the
// crack's half-width in metres and how strong it is (0-1); colour red: how far past the end of its line
// this part of the ribbon lies, so the ends round off and cracks meeting at a corner join up; green: how
// far the line's nearer end is, so its jitter dies away there and it meets the others.
shader_type spatial;
render_mode blend_mul, unshaded, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

// The ribbon's half-width, metres: the grimy band either side of the crack.
uniform float halo = 0.09;
uniform float halo_darkness = 0.25;
uniform vec3 halo_color : source_color = vec3(0.45, 0.41, 0.36);
uniform float darkness = 0.85;
uniform vec3 core_color : source_color = vec3(0.23, 0.2, 0.17);
// How much lighter the broken edge just outside the crack is.
uniform float rim = 0.12;
uniform float fade_start = 30.0;
uniform float fade_end = 55.0;

varying vec2 crack;
varying float beyond;
varying float to_end;

float hash(float n) {
	return fract(sin(n) * 43758.5453);
}

float wave(float x) {
	float i = floor(x);
	float f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(hash(i), hash(i + 1.0), f);
}

void vertex() {
	crack = UV2;
	beyond = COLOR.r;
	to_end = COLOR.g;
}

void fragment() {
	float s = UV.x;
	// Jagged: the line zigzags across by a centimetre or so and opens and closes along its length, with
	// here and there a stretch where its edges have crumbled.
	float jag = (wave(s * 9.0) - 0.5) * 0.026 + (wave(s * 31.0 + 5.0) - 0.5) * 0.01 + (wave(s * 97.0 + 2.0) - 0.5) * 0.004;
	float across = UV.y * halo + jag * smoothstep(0.0, 0.12, to_end);
	float d = length(vec2(beyond, across));
	float w = crack.x * (0.55 + 0.9 * wave(s * 4.3 + 3.0)) * (1.0 + 1.3 * smoothstep(0.72, 0.95, wave(s * 1.7 + 11.0)));
	// Thinner than a pixel, the line is drawn half a pixel wide and lighter, so it doesn't shimmer.
	float px = max(fwidth(d), 1e-4);
	float ww = max(w, px * 0.5);
	float core = (1.0 - smoothstep(ww - px * 0.5, ww + px * 0.5, d)) * (w / ww);
	float edge = clamp(1.0 - smoothstep(ww, ww + px + 0.006, d) - core, 0.0, 1.0);
	float band = 1.0 - smoothstep(0.0, halo, length(vec2(beyond, UV.y * halo)));
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(VERTEX));
	float k = crack.y * fade;
	vec3 grime = mix(vec3(1.0), halo_color, halo_darkness * band * band * k);
	vec3 dark = mix(vec3(1.0), core_color, darkness * core * k);
	ALBEDO = grime * dark * (1.0 + rim * edge * k);
}
