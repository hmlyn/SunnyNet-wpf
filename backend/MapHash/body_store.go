package MapHash

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sync"
	"sync/atomic"
	"time"
)

const bodyStoreMemoryLimit = 1024 * 1024

type BodyRef struct {
	ID         string `json:"-"`
	Size       int    `json:"-"`
	Stored     bool   `json:"-"`
	FileBacked bool   `json:"-"`
	Path       string `json:"-"`
}

type BodyStore struct {
	dir   string
	seq   atomic.Int64
	lock  sync.Mutex
	items map[string]BodyRef
}

func NewBodyStore() *BodyStore {
	dir := filepath.Join(os.TempDir(), "SunnyNetWpf", "body-store")
	_ = os.MkdirAll(dir, 0700)
	return &BodyStore{
		dir:   dir,
		items: make(map[string]BodyRef),
	}
}

func (s *BodyStore) Put(body []byte) BodyRef {
	ref := BodyRef{Size: len(body)}
	if s == nil || len(body) <= bodyStoreMemoryLimit {
		return ref
	}

	id := fmt.Sprintf("%d_%d.body", time.Now().UnixNano(), s.seq.Add(1))
	path := filepath.Join(s.dir, id)
	if err := os.WriteFile(path, body, 0600); err != nil {
		return ref
	}

	ref.ID = id
	ref.Stored = true
	ref.FileBacked = true
	ref.Path = path

	s.lock.Lock()
	s.items[id] = ref
	s.lock.Unlock()
	return ref
}

func (s *BodyStore) Read(ref BodyRef) ([]byte, bool) {
	if s == nil || !ref.Stored || ref.ID == "" {
		return nil, false
	}
	if ref.FileBacked {
		body, err := os.ReadFile(ref.Path)
		return body, err == nil
	}
	return nil, false
}

func (s *BodyStore) ReadRange(ref BodyRef, offset int64, count int) ([]byte, bool) {
	if s == nil || !ref.Stored || ref.ID == "" || offset < 0 || count < 1 {
		return nil, false
	}
	if !ref.FileBacked {
		return nil, false
	}
	file, err := os.Open(ref.Path)
	if err != nil {
		return nil, false
	}
	defer func() { _ = file.Close() }()

	if _, err = file.Seek(offset, io.SeekStart); err != nil {
		return nil, false
	}
	buf := make([]byte, count)
	n, err := file.Read(buf)
	if err != nil && err != io.EOF {
		return nil, false
	}
	return buf[:n], true
}

func (s *BodyStore) Delete(ref BodyRef) {
	if s == nil || !ref.Stored || ref.ID == "" {
		return
	}
	s.lock.Lock()
	delete(s.items, ref.ID)
	s.lock.Unlock()
	if ref.FileBacked && ref.Path != "" {
		_ = os.Remove(ref.Path)
	}
}
