package MapHash

type TLSFingerprint struct {
	SNI                 string
	ALPN                []string
	LegacyVersion       uint16
	LegacyVersionText   string
	HighestVersion      uint16
	HighestVersionText  string
	CipherSuites        []uint16
	Extensions          []uint16
	SupportedGroups     []uint16
	ECPointFormats      []uint8
	SignatureAlgorithms []uint16
	JA3Text             string
	JA3Hash             string
	JA3NText            string
	JA3NHash            string
	JA4                 string
	JA4O                string
	JA4R                string
	JA4RO               string
	RawClientHelloHex   string
}
