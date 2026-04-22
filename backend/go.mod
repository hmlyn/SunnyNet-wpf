module changeme

go 1.21

require (
	github.com/Trisia/gosysproxy v1.0.0
	github.com/andybalholm/brotli v1.0.5
	github.com/atotto/clipboard v0.1.4
	github.com/lwch/rdesktop v1.2.2
	github.com/mitchellh/go-ps v1.0.0
	github.com/qtgolang/SunnyNet v1.0.3
	github.com/traefik/yaegi v0.15.1
	golang.org/x/text v0.14.0
)

require (
	github.com/go-redis/redis v6.15.9+incompatible // indirect
	github.com/klauspost/compress v1.16.7 // indirect
	github.com/nxadm/tail v1.4.11 // indirect
	github.com/onsi/gomega v1.30.0 // indirect
	github.com/robertkrimen/otto v0.0.0-20221025135307-511d75fba9f8 // indirect
	github.com/stretchr/testify v1.8.1 // indirect
	golang.org/x/crypto v0.14.0 // indirect
	golang.org/x/image v0.14.0 // indirect
	golang.org/x/sys v0.15.0 // indirect
	google.golang.org/protobuf v1.28.1 // indirect
	gopkg.in/sourcemap.v1 v1.0.5 // indirect
)

replace github.com/qtgolang/SunnyNet => ../_source/SunnyNet-v1.0.3-patched
