package tls

// RawBytes returns a copy of the original ClientHello handshake message.
// The bytes start with the handshake type and uint24 length.
func (m *ClientHelloMsg) RawBytes() []byte {
	if m == nil || len(m.raw) == 0 {
		return nil
	}
	out := make([]byte, len(m.raw))
	copy(out, m.raw)
	return out
}
