package main

import (
	"crypto/md5"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"net"
	"sort"
	"strconv"
	"strings"

	"changeme/MapHash"
	sunnytls "github.com/qtgolang/SunnyNet/src/crypto/tls"
)

type tlsClientHelloData struct {
	legacyVersion       uint16
	cipherSuites        []uint16
	extensions          []uint16
	sni                 string
	supportedGroups     []uint16
	ecPointFormats      []uint8
	signatureAlgorithms []uint16
	supportedVersions   []uint16
	alpn                []string
	raw                 []byte
}

func BuildTLSFingerprint(hello *sunnytls.ClientHelloMsg) *MapHash.TLSFingerprint {
	if hello == nil {
		return nil
	}
	data, ok := parseTLSClientHello(hello.RawBytes())
	if !ok {
		return nil
	}

	ja3Ciphers := filterGreaseUint16(data.cipherSuites)
	ja3Extensions := filterGreaseUint16(data.extensions)
	ja3Groups := filterGreaseUint16(data.supportedGroups)
	ja3Points := data.ecPointFormats

	ja3Text := strings.Join([]string{
		strconv.Itoa(int(data.legacyVersion)),
		joinUint16Decimal(ja3Ciphers),
		joinUint16Decimal(ja3Extensions),
		joinUint16Decimal(ja3Groups),
		joinUint8Decimal(ja3Points),
	}, ",")

	ja3nExtensions := append([]uint16(nil), ja3Extensions...)
	sort.Slice(ja3nExtensions, func(i, j int) bool { return ja3nExtensions[i] < ja3nExtensions[j] })
	ja3nText := strings.Join([]string{
		strconv.Itoa(int(data.legacyVersion)),
		joinUint16Decimal(ja3Ciphers),
		joinUint16Decimal(ja3nExtensions),
		joinUint16Decimal(ja3Groups),
		joinUint8Decimal(ja3Points),
	}, ",")

	highestVersion := highestTLSVersion(data)
	ja4, ja4o, ja4r, ja4ro := buildJA4(data, highestVersion, ja3Ciphers, ja3Extensions)

	return &MapHash.TLSFingerprint{
		SNI:                 data.sni,
		ALPN:                data.alpn,
		LegacyVersion:       data.legacyVersion,
		LegacyVersionText:   tlsVersionText(data.legacyVersion),
		HighestVersion:      highestVersion,
		HighestVersionText:  tlsVersionText(highestVersion),
		CipherSuites:        ja3Ciphers,
		Extensions:          ja3Extensions,
		SupportedGroups:     ja3Groups,
		ECPointFormats:      ja3Points,
		SignatureAlgorithms: filterGreaseUint16(data.signatureAlgorithms),
		JA3Text:             ja3Text,
		JA3Hash:             md5Hex(ja3Text),
		JA3NText:            ja3nText,
		JA3NHash:            md5Hex(ja3nText),
		JA4:                 ja4,
		JA4O:                ja4o,
		JA4R:                ja4r,
		JA4RO:               ja4ro,
		RawClientHelloHex:   strings.ToUpper(hex.EncodeToString(data.raw)),
	}
}

func parseTLSClientHello(raw []byte) (tlsClientHelloData, bool) {
	var result tlsClientHelloData
	if len(raw) < 4 || raw[0] != 0x01 {
		return result, false
	}
	declaredLength := int(raw[1])<<16 | int(raw[2])<<8 | int(raw[3])
	if declaredLength <= 0 || len(raw) < declaredLength+4 {
		return result, false
	}
	body := raw[4 : 4+declaredLength]
	result.raw = append([]byte(nil), raw[:4+declaredLength]...)

	offset := 0
	readUint8 := func() (uint8, bool) {
		if offset >= len(body) {
			return 0, false
		}
		value := body[offset]
		offset++
		return value, true
	}
	readUint16 := func() (uint16, bool) {
		if offset+2 > len(body) {
			return 0, false
		}
		value := uint16(body[offset])<<8 | uint16(body[offset+1])
		offset += 2
		return value, true
	}
	readBytes := func(length int) ([]byte, bool) {
		if length < 0 || offset+length > len(body) {
			return nil, false
		}
		value := body[offset : offset+length]
		offset += length
		return value, true
	}

	var ok bool
	if result.legacyVersion, ok = readUint16(); !ok {
		return result, false
	}
	if _, ok = readBytes(32); !ok {
		return result, false
	}
	sessionIDLength, ok := readUint8()
	if !ok {
		return result, false
	}
	if _, ok = readBytes(int(sessionIDLength)); !ok {
		return result, false
	}

	cipherBytesLength, ok := readUint16()
	if !ok || cipherBytesLength%2 != 0 {
		return result, false
	}
	cipherBytes, ok := readBytes(int(cipherBytesLength))
	if !ok {
		return result, false
	}
	for i := 0; i+1 < len(cipherBytes); i += 2 {
		result.cipherSuites = append(result.cipherSuites, uint16(cipherBytes[i])<<8|uint16(cipherBytes[i+1]))
	}

	compressionLength, ok := readUint8()
	if !ok {
		return result, false
	}
	if _, ok = readBytes(int(compressionLength)); !ok {
		return result, false
	}
	if offset == len(body) {
		return result, true
	}

	extensionsLength, ok := readUint16()
	if !ok || offset+int(extensionsLength) > len(body) {
		return result, false
	}
	extensionsEnd := offset + int(extensionsLength)
	for offset < extensionsEnd {
		extensionID, ok := readUint16()
		if !ok {
			return result, false
		}
		extensionLength, ok := readUint16()
		if !ok {
			return result, false
		}
		extensionData, ok := readBytes(int(extensionLength))
		if !ok {
			return result, false
		}
		result.extensions = append(result.extensions, extensionID)
		parseTLSClientHelloExtension(&result, extensionID, extensionData)
	}

	return result, offset == extensionsEnd
}

func parseTLSClientHelloExtension(result *tlsClientHelloData, extensionID uint16, data []byte) {
	switch extensionID {
	case 0x0000:
		result.sni = parseTLSServerName(data)
	case 0x000a:
		result.supportedGroups = parseUint16VectorWithUint16Length(data)
	case 0x000b:
		result.ecPointFormats = parseUint8VectorWithUint8Length(data)
	case 0x000d:
		result.signatureAlgorithms = parseUint16VectorWithUint16Length(data)
	case 0x0010:
		result.alpn = parseTLSALPN(data)
	case 0x002b:
		result.supportedVersions = parseTLSSupportedVersions(data)
	}
}

func parseTLSServerName(data []byte) string {
	if len(data) < 2 {
		return ""
	}
	listLength := int(data[0])<<8 | int(data[1])
	offset := 2
	end := offset + listLength
	if end > len(data) {
		return ""
	}
	for offset+3 <= end {
		nameType := data[offset]
		offset++
		nameLength := int(data[offset])<<8 | int(data[offset+1])
		offset += 2
		if offset+nameLength > end {
			return ""
		}
		if nameType == 0 {
			return string(data[offset : offset+nameLength])
		}
		offset += nameLength
	}
	return ""
}

func parseTLSALPN(data []byte) []string {
	if len(data) < 2 {
		return nil
	}
	listLength := int(data[0])<<8 | int(data[1])
	offset := 2
	end := offset + listLength
	if end > len(data) {
		return nil
	}
	var protocols []string
	for offset < end {
		length := int(data[offset])
		offset++
		if length == 0 || offset+length > end {
			return protocols
		}
		protocols = append(protocols, string(data[offset:offset+length]))
		offset += length
	}
	return protocols
}

func parseTLSSupportedVersions(data []byte) []uint16 {
	if len(data) == 0 {
		return nil
	}
	length := int(data[0])
	if length%2 != 0 || length+1 > len(data) {
		return nil
	}
	var versions []uint16
	for offset := 1; offset+1 < 1+length; offset += 2 {
		versions = append(versions, uint16(data[offset])<<8|uint16(data[offset+1]))
	}
	return versions
}

func parseUint16VectorWithUint16Length(data []byte) []uint16 {
	if len(data) < 2 {
		return nil
	}
	length := int(data[0])<<8 | int(data[1])
	if length%2 != 0 || length+2 > len(data) {
		return nil
	}
	values := make([]uint16, 0, length/2)
	for offset := 2; offset+1 < 2+length; offset += 2 {
		values = append(values, uint16(data[offset])<<8|uint16(data[offset+1]))
	}
	return values
}

func parseUint8VectorWithUint8Length(data []byte) []uint8 {
	if len(data) == 0 {
		return nil
	}
	length := int(data[0])
	if length+1 > len(data) {
		return nil
	}
	values := make([]uint8, length)
	copy(values, data[1:1+length])
	return values
}

func buildJA4(data tlsClientHelloData, highestVersion uint16, ciphers []uint16, extensions []uint16) (string, string, string, string) {
	ja4Prefix := fmt.Sprintf(
		"t%s%s%02d%02d%s",
		ja4VersionText(highestVersion),
		ja4SNIMarker(data.sni),
		min(len(ciphers), 99),
		min(len(extensions), 99),
		ja4ALPN(data.alpn),
	)

	sortedCiphers := append([]uint16(nil), ciphers...)
	sort.Slice(sortedCiphers, func(i, j int) bool { return sortedCiphers[i] < sortedCiphers[j] })
	originalCiphers := append([]uint16(nil), ciphers...)

	sortedExtensions := make([]uint16, 0, len(extensions))
	for _, extension := range extensions {
		if extension == 0x0000 || extension == 0x0010 {
			continue
		}
		sortedExtensions = append(sortedExtensions, extension)
	}
	sort.Slice(sortedExtensions, func(i, j int) bool { return sortedExtensions[i] < sortedExtensions[j] })
	originalExtensions := append([]uint16(nil), extensions...)

	signatures := filterGreaseUint16(data.signatureAlgorithms)

	sortedCipherText := joinUint16Hex(sortedCiphers)
	originalCipherText := joinUint16Hex(originalCiphers)
	sortedExtensionText := joinUint16Hex(sortedExtensions)
	originalExtensionText := joinUint16Hex(originalExtensions)
	signatureText := joinUint16Hex(signatures)
	sortedExtensionSegment := ja4ExtensionSegment(sortedExtensionText, signatureText)
	originalExtensionSegment := ja4ExtensionSegment(originalExtensionText, signatureText)

	ja4 := ja4Prefix + "_" + ja4HashPart(sortedCipherText) + "_" + ja4HashPart(sortedExtensionSegment)
	ja4o := ja4Prefix + "_" + ja4HashPart(originalCipherText) + "_" + ja4HashPart(originalExtensionSegment)
	ja4r := ja4Prefix + "_" + sortedCipherText + "_" + sortedExtensionSegment
	ja4ro := ja4Prefix + "_" + originalCipherText + "_" + originalExtensionSegment
	return ja4, ja4o, ja4r, ja4ro
}

func highestTLSVersion(data tlsClientHelloData) uint16 {
	highest := data.legacyVersion
	for _, version := range data.supportedVersions {
		if isGREASE(version) {
			continue
		}
		if version > highest {
			highest = version
		}
	}
	return highest
}

func filterGreaseUint16(values []uint16) []uint16 {
	filtered := make([]uint16, 0, len(values))
	for _, value := range values {
		if isGREASE(value) {
			continue
		}
		filtered = append(filtered, value)
	}
	return filtered
}

func isGREASE(value uint16) bool {
	return value&0x0f0f == 0x0a0a && byte(value>>8) == byte(value)
}

func md5Hex(text string) string {
	sum := md5.Sum([]byte(text))
	return hex.EncodeToString(sum[:])
}

func ja4ExtensionSegment(extensionText string, signatureText string) string {
	if signatureText == "" {
		return extensionText
	}
	return extensionText + "_" + signatureText
}

func ja4HashPart(text string) string {
	if text == "" {
		return "000000000000"
	}
	return sha256First12(text)
}

func sha256First12(text string) string {
	sum := sha256.Sum256([]byte(text))
	return hex.EncodeToString(sum[:])[:12]
}

func joinUint16Decimal(values []uint16) string {
	parts := make([]string, 0, len(values))
	for _, value := range values {
		parts = append(parts, strconv.Itoa(int(value)))
	}
	return strings.Join(parts, "-")
}

func joinUint8Decimal(values []uint8) string {
	parts := make([]string, 0, len(values))
	for _, value := range values {
		parts = append(parts, strconv.Itoa(int(value)))
	}
	return strings.Join(parts, "-")
}

func joinUint16Hex(values []uint16) string {
	parts := make([]string, 0, len(values))
	for _, value := range values {
		parts = append(parts, fmt.Sprintf("%04x", value))
	}
	return strings.Join(parts, ",")
}

func tlsVersionText(version uint16) string {
	switch version {
	case 0x0300:
		return "SSL 3.0"
	case 0x0301:
		return "TLS 1.0"
	case 0x0302:
		return "TLS 1.1"
	case 0x0303:
		return "TLS 1.2"
	case 0x0304:
		return "TLS 1.3"
	default:
		if version == 0 {
			return ""
		}
		return fmt.Sprintf("0x%04X", version)
	}
}

func ja4VersionText(version uint16) string {
	switch version {
	case 0x0304:
		return "13"
	case 0x0303:
		return "12"
	case 0x0302:
		return "11"
	case 0x0301:
		return "10"
	case 0x0300:
		return "s3"
	default:
		return fmt.Sprintf("%02x", version&0xff)
	}
}

func ja4SNIMarker(sni string) string {
	if strings.TrimSpace(sni) == "" || net.ParseIP(sni) != nil {
		return "i"
	}
	return "d"
}

func ja4ALPN(protocols []string) string {
	if len(protocols) == 0 {
		return "00"
	}
	switch strings.ToLower(protocols[0]) {
	case "h2":
		return "h2"
	case "h3":
		return "h3"
	case "http/1.1":
		return "h1"
	case "http/1.0":
		return "h0"
	}
	protocol := protocols[0]
	runes := make([]rune, 0, len(protocol))
	for _, r := range protocol {
		if (r >= 'a' && r <= 'z') || (r >= 'A' && r <= 'Z') || (r >= '0' && r <= '9') {
			runes = append(runes, r)
		}
	}
	if len(runes) == 0 {
		return "00"
	}
	if len(runes) == 1 {
		return strings.ToLower(string([]rune{runes[0], '0'}))
	}
	return strings.ToLower(string([]rune{runes[0], runes[len(runes)-1]}))
}
