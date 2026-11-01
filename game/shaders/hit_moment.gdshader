// The moment you're hit (game/ui/HitMoment.cs): the view drains of colour, a flash, and the paint's colour floods in
// from the edge it came from, with a ragged rim, while the vignette darkens. Every strength comes from the C# side,
// already scaled by the settings' hit effect.
shader_type canvas_item;

uniform sampler2D screen : hint_screen_texture, filter_linear_mipmap;
uniform float desaturate : hint_range(0.0, 1.0) = 0.0;
uniform float tint : hint_range(0.0, 1.0) = 0.0;
uniform float vignette : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4 paint : source_color = vec4(1.0, 0.4, 0.1, 1.0);
// Towards where the paint came from, on the screen (x right, y down; zero: all round).
uniform vec2 from_dir = vec2(0.0, 0.0);
uniform float seed = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7)) + seed * 17.13) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec3 c = texture(screen, SCREEN_UV).rgb;
	float grey = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(c, vec3(grey), desaturate);

	// Aspect-correct position from the middle: about ±1.8 across, ±1 up and down on a wide screen.
	vec2 p = (SCREEN_UV - 0.5) * 2.0 * vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
	float r = length(p);
	float side = length(from_dir) > 0.0 ? max(dot(p / max(r, 1e-4), normalize(from_dir)), 0.0) : 0.6;
	float rim = r + 0.22 * (noise(p * 3.2) - 0.5) + 0.08 * (noise(p * 11.0) - 0.5);
	// The paint floods further in on the side it came from.
	float reach = mix(1.25, 0.55, side * side);
	float flood = smoothstep(reach, reach + 0.35, rim);
	c = mix(c, paint.rgb * (0.45 + 0.55 * grey), clamp(flood * tint, 0.0, 1.0));
	c *= 1.0 - vignette * 0.85 * smoothstep(0.55, 1.6, r);
	c = mix(c, vec3(1.0, 0.97, 0.92), flash);
	COLOR = vec4(c, 1.0);
}
