package Api

import (
	"github.com/qtgolang/SunnyNet/public"
	"sync"
)

var Queue = make(map[string]*ArrayQueue)
var QueueLock sync.Mutex

type ArrayQueue struct {
	Array [][]byte
	Size  int
	Lock  sync.Mutex
}

func (q *ArrayQueue) IsEmpty() bool {
	q.Lock.Lock()
	defer q.Lock.Unlock()
	return q.Size == 0
}
func (q *ArrayQueue) Empty() {
	q.Lock.Lock()
	defer q.Lock.Unlock()
	q.Size = 0
	q.Array = make([][]byte, 0)
}
func (q *ArrayQueue) Length() int {
	q.Lock.Lock()
	defer q.Lock.Unlock()
	return q.Size
}
func (q *ArrayQueue) Push(v []byte) {
	q.Lock.Lock()
	defer q.Lock.Unlock()
	q.Array = append(q.Array, v)
	q.Size++
}
func (q *ArrayQueue) Pull() []byte {
	q.Lock.Lock()
	defer q.Lock.Unlock()
	if q.Size == 0 {
		return []byte{}
	}
	v := q.Array[0]
	q.Array = q.Array[1:]
	q.Size--
	return v
}

// CreateQueue
// 创建队列
func CreateQueue(name string) {
	QueueLock.Lock()
	if Queue[name] == nil {
		Queue[name] = new(ArrayQueue)
	} else {
		Queue[name].Empty()
	}
	QueueLock.Unlock()
	return
}

// QueueIsEmpty
// 队列是否为空
func QueueIsEmpty(name string) bool {
	QueueLock.Lock()
	Object := Queue[name]
	QueueLock.Unlock()
	if Object == nil {
		return true
	}
	return Object.IsEmpty()
}

// QueueRelease
// 清空销毁队列
func QueueRelease(name string) {
	QueueLock.Lock()
	Object := Queue[name]
	QueueLock.Unlock()
	if Object == nil {
		return
	}
	Object.Empty()
	QueueLock.Lock()
	delete(Queue, name)
	QueueLock.Unlock()
}

// QueueLength
// 取队列长度
func QueueLength(name string) int {
	QueueLock.Lock()
	Object := Queue[name]
	QueueLock.Unlock()
	if Object == nil {
		return 0
	}
	return Object.Length()
}

// QueuePush
// 加入队列
func QueuePush(name string, val uintptr, valLen int) {
	data := public.CStringToBytes(val, valLen)
	QueueLock.Lock()
	Object := Queue[name]
	QueueLock.Unlock()
	if Object == nil {
		return
	}
	Object.Push(data)
}

// QueuePull
// 队列弹出
func QueuePull(name string) uintptr {
	QueueLock.Lock()
	Object := Queue[name]
	QueueLock.Unlock()
	if Object == nil {
		return 0
	}
	bx := Object.Pull()
	if len(bx) < 1 {
		return 0
	}
	return public.PointerPtr(public.BytesCombine(public.IntToBytes(len(bx)), bx))
}
