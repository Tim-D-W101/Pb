// Birds circling overhead (Birds): dark silhouettes against the sky. Each wing turns about its root
// along the body: held a little up while gliding, beating in bursts (instance custom data: red =
// wingbeat phase, green = how much it's flapping, 0–1). Unshaded: from below a bird is a shape, not a
// lit surface; fog still fades it.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(0.1);
// Wingbeats per second, how far a beat swings the wing (radians), and the glide's dihedral.
uniform float beat_hz = 4.0;
uniform float beat_swing = 0.75;
uniform float dihedral = 0.14;
// Where the wings join the body (metres out from the middle line).
uniform float root = 0.035;

void vertex() {
	float side = sign(VERTEX.x);
	float reach = abs(VERTEX.x) - root;
	if (reach > 0.0) {
		float flap = INSTANCE_CUSTOM.g;
		float angle = dihedral + flap * beat_swing * sin(TIME * beat_hz * 6.2831853 + INSTANCE_CUSTOM.r);
		// The wing's tip leads its root a little on the downstroke.
		angle += flap * 0.25 * beat_swing * sin(TIME * beat_hz * 6.2831853 + INSTANCE_CUSTOM.r - 1.2) * reach;
		VERTEX.x = side * (root + reach * cos(angle));
		VERTEX.y += reach * sin(angle);
	}
}

void fragment() {
	ALBEDO = color;
}
