package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"

	"github.com/jhump/protoreflect/desc"
	"github.com/jhump/protoreflect/desc/protoparse"
	"github.com/jhump/protoreflect/dynamic"
	"google.golang.org/protobuf/proto"
	"google.golang.org/protobuf/types/descriptorpb"
)

type protobufSchemaCache struct {
	lock      sync.RWMutex
	directory string
	messages  map[string]*desc.MessageDescriptor
	types     []string
}

var globalProtobufSchemaCache protobufSchemaCache

func importProtobufSchema(path string) ([]string, string, error) {
	source, err := resolveProtobufSchemaSource(path)
	if err != nil {
		return nil, "", err
	}

	fileDescriptors := make([]*desc.FileDescriptor, 0, len(source.protoFiles)+len(source.descriptorFiles))

	if len(source.protoFiles) > 0 {
		parser := protoparse.Parser{
			ImportPaths:           []string{source.root},
			InferImportPaths:      true,
			IncludeSourceCodeInfo: false,
		}

		descriptors, err := parser.ParseFiles(source.protoFiles...)
		if err != nil {
			return nil, "", fmt.Errorf("导入 .proto 定义失败: %w", err)
		}
		fileDescriptors = append(fileDescriptors, descriptors...)
	}

	if len(source.descriptorFiles) > 0 {
		descriptors, err := loadProtobufDescriptorFiles(source.descriptorFiles)
		if err != nil {
			return nil, "", err
		}
		fileDescriptors = append(fileDescriptors, descriptors...)
	}

	messageMap := make(map[string]*desc.MessageDescriptor)
	for _, fileDescriptor := range fileDescriptors {
		for _, messageDescriptor := range fileDescriptor.GetMessageTypes() {
			appendMessageDescriptors(messageMap, messageDescriptor)
		}
	}

	if len(messageMap) == 0 {
		return nil, "", errors.New("当前目录未找到可解析的消息类型")
	}

	typeList := make([]string, 0, len(messageMap))
	for name := range messageMap {
		typeList = append(typeList, name)
	}
	sort.Strings(typeList)

	globalProtobufSchemaCache.lock.Lock()
	globalProtobufSchemaCache.directory = source.cacheKey
	globalProtobufSchemaCache.messages = messageMap
	globalProtobufSchemaCache.types = typeList
	globalProtobufSchemaCache.lock.Unlock()

	return typeList, source.displayPath, nil
}

func protobufToJsonBySchema(data []byte, skip int, path string, messageType string) (string, error) {
	if len(data) == 0 {
		return "", errors.New("当前数据为空")
	}
	if skip < 0 || skip > len(data) {
		return "", errors.New("跳过字节超出当前数据范围")
	}

	schema, err := ensureProtobufSchema(path)
	if err != nil {
		return "", err
	}

	targetType := strings.TrimSpace(messageType)
	if targetType == "" {
		return "", errors.New("请选择消息类型")
	}

	messageDescriptor := findMessageDescriptor(schema.messages, targetType)
	if messageDescriptor == nil {
		return "", fmt.Errorf("未找到消息类型: %s", targetType)
	}

	message := dynamic.NewMessage(messageDescriptor)
	if err = message.Unmarshal(data[skip:]); err != nil {
		return "", fmt.Errorf("按消息类型解析失败: %w", err)
	}

	rawJson, err := message.MarshalJSON()
	if err != nil {
		return "", fmt.Errorf("Protobuf 转 JSON 失败: %w", err)
	}

	var formatted bytes.Buffer
	if err = json.Indent(&formatted, rawJson, "", "\t"); err != nil {
		return strings.ReplaceAll(string(rawJson), "\n", "\r\n"), nil
	}
	return strings.ReplaceAll(formatted.String(), "\n", "\r\n"), nil
}

func ensureProtobufSchema(path string) (*protobufSchemaCacheSnapshot, error) {
	source, err := resolveProtobufSchemaSource(path)
	if err != nil {
		return nil, err
	}

	globalProtobufSchemaCache.lock.RLock()
	if strings.EqualFold(globalProtobufSchemaCache.directory, source.cacheKey) && len(globalProtobufSchemaCache.messages) > 0 {
		snapshot := &protobufSchemaCacheSnapshot{
			directory: globalProtobufSchemaCache.directory,
			messages:  globalProtobufSchemaCache.messages,
			types:     globalProtobufSchemaCache.types,
		}
		globalProtobufSchemaCache.lock.RUnlock()
		return snapshot, nil
	}
	globalProtobufSchemaCache.lock.RUnlock()

	types, _, err := importProtobufSchema(source.displayPath)
	if err != nil {
		return nil, err
	}

	globalProtobufSchemaCache.lock.RLock()
	defer globalProtobufSchemaCache.lock.RUnlock()
	return &protobufSchemaCacheSnapshot{
		directory: globalProtobufSchemaCache.directory,
		messages:  globalProtobufSchemaCache.messages,
		types:     types,
	}, nil
}

type protobufSchemaCacheSnapshot struct {
	directory string
	messages  map[string]*desc.MessageDescriptor
	types     []string
}

type protobufSchemaSource struct {
	root            string
	displayPath     string
	cacheKey        string
	protoFiles      []string
	descriptorFiles []string
}

func resolveProtobufSchemaSource(path string) (*protobufSchemaSource, error) {
	cleaned := strings.TrimSpace(path)
	if cleaned == "" {
		return nil, errors.New("Protobuf 目录不能为空")
	}

	fileInfo, err := os.Stat(cleaned)
	if err != nil {
		return nil, fmt.Errorf("Protobuf 路径无效: %w", err)
	}

	if fileInfo.IsDir() {
		root := filepath.Clean(cleaned)
		protoFiles, descriptorFiles, err := collectProtobufSchemaFiles(root)
		if err != nil {
			return nil, err
		}
		return &protobufSchemaSource{
			root:            root,
			displayPath:     root,
			cacheKey:        root,
			protoFiles:      protoFiles,
			descriptorFiles: descriptorFiles,
		}, nil
	}

	absolutePath := filepath.Clean(cleaned)
	extension := strings.ToLower(filepath.Ext(absolutePath))
	root := filepath.Dir(absolutePath)
	switch extension {
	case ".proto":
		relativePath, err := filepath.Rel(root, absolutePath)
		if err != nil {
			return nil, err
		}
		return &protobufSchemaSource{
			root:        root,
			displayPath: absolutePath,
			cacheKey:    absolutePath,
			protoFiles:  []string{filepath.ToSlash(relativePath)},
		}, nil
	case ".pb", ".desc", ".protoset":
		return &protobufSchemaSource{
			root:            root,
			displayPath:     absolutePath,
			cacheKey:        absolutePath,
			descriptorFiles: []string{absolutePath},
		}, nil
	}

	return nil, errors.New("请选择包含 .proto 或 .pb 描述文件的目录")
}

func collectProtobufSchemaFiles(root string) ([]string, []string, error) {
	protoFiles := make([]string, 0, 32)
	descriptorFiles := make([]string, 0, 32)
	err := filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() {
			return nil
		}
		extension := strings.ToLower(filepath.Ext(entry.Name()))
		switch extension {
		case ".proto":
			relativePath, err := filepath.Rel(root, path)
			if err != nil {
				return err
			}
			protoFiles = append(protoFiles, filepath.ToSlash(relativePath))
		case ".pb", ".desc", ".protoset":
			descriptorFiles = append(descriptorFiles, filepath.Clean(path))
		}
		return nil
	})
	if err != nil {
		return nil, nil, err
	}
	if len(protoFiles) == 0 && len(descriptorFiles) == 0 {
		return nil, nil, errors.New("当前目录未找到 .proto 或 .pb 描述文件")
	}
	sort.Strings(protoFiles)
	sort.Strings(descriptorFiles)
	return protoFiles, descriptorFiles, nil
}

func loadProtobufDescriptorFiles(files []string) ([]*desc.FileDescriptor, error) {
	fileMap := make(map[string]*descriptorpb.FileDescriptorProto)
	anonymousIndex := 0
	for _, file := range files {
		data, err := os.ReadFile(file)
		if err != nil {
			return nil, fmt.Errorf("读取 .pb 描述文件失败 %s: %w", file, err)
		}

		fileSet := &descriptorpb.FileDescriptorSet{}
		if err = proto.Unmarshal(data, fileSet); err != nil {
			return nil, fmt.Errorf("解析 .pb 描述文件失败 %s: %w", file, err)
		}
		if len(fileSet.File) == 0 {
			return nil, fmt.Errorf(".pb 描述文件未包含 FileDescriptorSet: %s", file)
		}

		for _, fileDescriptor := range fileSet.File {
			name := strings.TrimSpace(fileDescriptor.GetName())
			if name == "" {
				anonymousIndex++
				name = fmt.Sprintf("anonymous_%d.proto", anonymousIndex)
				fileDescriptor.Name = &name
			}
			if _, exists := fileMap[name]; !exists {
				fileMap[name] = fileDescriptor
			}
		}
	}

	fileSet := &descriptorpb.FileDescriptorSet{
		File: make([]*descriptorpb.FileDescriptorProto, 0, len(fileMap)),
	}
	names := make([]string, 0, len(fileMap))
	for name := range fileMap {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		fileSet.File = append(fileSet.File, fileMap[name])
	}

	fileDescriptors, err := desc.CreateFileDescriptorsFromSet(fileSet)
	if err != nil {
		return nil, fmt.Errorf("加载 .pb 描述结构失败: %w", err)
	}

	descriptors := make([]*desc.FileDescriptor, 0, len(fileDescriptors))
	for _, name := range names {
		if fileDescriptor := fileDescriptors[name]; fileDescriptor != nil {
			descriptors = append(descriptors, fileDescriptor)
		}
	}
	return descriptors, nil
}

func appendMessageDescriptors(target map[string]*desc.MessageDescriptor, descriptor *desc.MessageDescriptor) {
	if descriptor == nil {
		return
	}
	target[descriptor.GetFullyQualifiedName()] = descriptor
	for _, nested := range descriptor.GetNestedMessageTypes() {
		appendMessageDescriptors(target, nested)
	}
}

func findMessageDescriptor(messages map[string]*desc.MessageDescriptor, messageType string) *desc.MessageDescriptor {
	if messages == nil {
		return nil
	}

	if descriptor := messages[messageType]; descriptor != nil {
		return descriptor
	}

	var match *desc.MessageDescriptor
	needle := "." + messageType
	for name, descriptor := range messages {
		if strings.EqualFold(name, messageType) || strings.HasSuffix(strings.ToLower(name), strings.ToLower(needle)) {
			if match != nil {
				return nil
			}
			match = descriptor
		}
	}
	return match
}
